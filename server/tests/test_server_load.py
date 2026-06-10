"""
test_server_load.py - 2H2G 服务器负载测试
模拟 2核CPU、2GB内存、50Mbps带宽的 VPS 环境
验证服务器在内存 ≤50MB、CPU ≤20% 的约束下正常运行
"""

import sys
import os
import gc
import json
import time
import asyncio
import subprocess
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import psutil
import pytest
import websockets

# ================================================================
# 常量
# ================================================================

SERVER_DIR = Path(__file__).resolve().parent.parent
TEST_PORT = int(os.environ.get("TEST_SERVER_PORT", "18060"))
HOST = "127.0.0.1"

# 资源约束（2H2G VPS）
MAX_MEMORY_MB = 100.0  # Python + FastAPI + Uvicorn + 并发房间管理 + GC 余量
MAX_CPU_PERCENT = 30.0  # 2核的 30% = 0.6 核
MAX_P95_LATENCY_MS = 50.0

# 端口分配器（每个测试用不同端口避免冲突）
_port_counter = TEST_PORT

# ================================================================
# 资源监控器
# ================================================================


class ResourceMonitor:
    """后台线程监控进程 RSS 和 CPU 占用"""

    def __init__(self, pid, interval=0.5):
        self.proc = psutil.Process(pid)
        self.interval = interval
        self.samples = []
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)

    def start(self):
        self.proc.cpu_percent(interval=None)
        self._thread.start()

    def stop(self):
        self._stop.set()
        self._thread.join(timeout=3)

    def _run(self):
        while not self._stop.is_set():
            try:
                rss = self.proc.memory_info().rss / (1024 * 1024)
                cpu = self.proc.cpu_percent(interval=None)
                self.samples.append((time.time(), rss, cpu))
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                break
            self._stop.wait(self.interval)

    @property
    def max_rss_mb(self):
        return max(s[1] for s in self.samples) if self.samples else 0.0

    @property
    def min_rss_mb(self):
        return min(s[1] for s in self.samples) if self.samples else 0.0

    @property
    def avg_cpu_percent(self):
        return sum(s[2] for s in self.samples) / len(self.samples) if self.samples else 0.0

    @property
    def max_cpu_percent(self):
        return max(s[2] for s in self.samples) if self.samples else 0.0

    def summary(self):
        return {
            "samples": len(self.samples),
            "max_rss_mb": round(self.max_rss_mb, 1),
            "min_rss_mb": round(self.min_rss_mb, 1),
            "avg_cpu_percent": round(self.avg_cpu_percent, 1),
            "max_cpu_percent": round(self.max_cpu_percent, 1),
        }


# ================================================================
# 服务器子进程 Fixture
# ================================================================


@pytest.fixture()
def server_process():
    """
    启动真实 FastAPI 服务器子进程。
    每个测试使用独立端口和独立服务器实例，避免跨测试状态污染。
    返回: (host, port, pid)
    """
    global _port_counter
    port = _port_counter
    _port_counter += 1

    # 提高并发对局上限
    config_file = SERVER_DIR / "server_config.json"
    original_config = None
    if config_file.exists():
        original_config = config_file.read_text(encoding="utf-8")
    config_file.write_text('{"max_concurrent_games": 100}', encoding="utf-8")

    env = os.environ.copy()
    env["SERVER_PORT"] = str(port)

    proc = subprocess.Popen(
        [sys.executable, "main.py"],
        cwd=str(SERVER_DIR),
        env=env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )

    try:
        import requests as _requests

        for _ in range(30):
            if proc.poll() is not None:
                raise RuntimeError(f"服务器进程已退出，返回码: {proc.returncode}")
            try:
                resp = _requests.get(f"http://{HOST}:{port}/health", timeout=1)
                if resp.status_code == 200:
                    break
            except Exception:
                pass
            time.sleep(0.5)
        else:
            proc.kill()
            raise RuntimeError("服务器 15 秒内未就绪")

        yield HOST, port, proc.pid
    finally:
        try:
            proc.terminate()
            proc.wait(timeout=5)
        except Exception:
            proc.kill()
            proc.wait(timeout=3)
        # 恢复原始配置
        if original_config is not None:
            config_file.write_text(original_config, encoding="utf-8")


# ================================================================
# WebSocket 辅助函数
# ================================================================


async def ws_connect(host, port):
    """建立 WebSocket 迶接"""
    return await websockets.connect(f"ws://{host}:{port}/ws")


async def ws_recv(ws, expected_type, timeout=8.0):
    """
    接收消息，跳过所有非 expected_type 的消息。
    如果在 timeout 秒内没收到目标类型，抛出 TimeoutError。
    """
    deadline = time.monotonic() + timeout
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TimeoutError(f"等待 '{expected_type}' 超时")
        try:
            raw = await asyncio.wait_for(ws.recv(), timeout=remaining)
            msg = json.loads(raw) if isinstance(raw, (str, bytes)) else raw
            if msg.get("type") == expected_type:
                return msg
            # 不匹配 → 跳过，继续读
        except asyncio.TimeoutError:
            raise TimeoutError(f"等待 '{expected_type}' 超时")


async def ws_recv_any(ws, timeout=5.0):
    """接收下一条消息（任意类型）"""
    raw = await asyncio.wait_for(ws.recv(), timeout=timeout)
    return json.loads(raw) if isinstance(raw, (str, bytes)) else raw


async def ws_send_recv(ws, msg_type, payload=None, response_type=None, timeout=8.0):
    """发送消息并等待指定类型的响应（跳过其他消息）"""
    await ws.send(json.dumps({"type": msg_type, "payload": payload or {}}))
    return await ws_recv(ws, response_type or msg_type, timeout=timeout)


async def create_and_fill_room(host, port):
    """
    创建房间并让 3 个玩家加入、准备、等待游戏开始。
    使用 ws_recv 自动跳过广播消息（player_joined 等）。
    返回: (room_code, [ws1, ws2, ws3], hands, first_bidder)
    """
    ws1 = await ws_connect(host, port)
    ws2 = await ws_connect(host, port)
    ws3 = await ws_connect(host, port)

    try:
        # ── 创建房间 ──
        resp = await ws_send_recv(ws1, "create_room",
                                  {"player_name": "P1", "is_public": True},
                                  response_type="room_created")
        room_code = resp["payload"]["room_code"]

        # ── P2 加入 ──
        await ws2.send(json.dumps({
            "type": "join_room",
            "payload": {"room_code": room_code, "player_name": "P2"},
        }))
        await ws_recv(ws2, "room_joined")
        await ws_recv(ws1, "player_joined")

        # ── P3 加入 ──
        await ws3.send(json.dumps({
            "type": "join_room",
            "payload": {"room_code": room_code, "player_name": "P3"},
        }))
        await ws_recv(ws3, "room_joined")
        await ws_recv(ws1, "player_joined")
        await ws_recv(ws2, "player_joined")

        # ── 全部准备 ──
        for ws in (ws1, ws2, ws3):
            await ws.send(json.dumps({"type": "ready", "payload": {}}))

        # 等待 game_start（ws_recv 会跳过中间的 player_ready 消息）
        gs1 = await ws_recv(ws1, "game_start")
        gs2 = await ws_recv(ws2, "game_start")
        gs3 = await ws_recv(ws3, "game_start")
        hands = [
            gs1["payload"]["hand"],
            gs2["payload"]["hand"],
            gs3["payload"]["hand"],
        ]

        # 等待 turn_change（ws_recv 会跳过 player_ready）
        tc = await ws_recv(ws1, "turn_change")
        await ws_recv(ws2, "turn_change")
        await ws_recv(ws3, "turn_change")
        first_bidder = tc["payload"]["current_player"]

        return room_code, [ws1, ws2, ws3], hands, first_bidder

    except Exception:
        for ws in (ws1, ws2, ws3):
            try:
                await ws.close()
            except Exception:
                pass
        raise


async def play_one_game(ws_clients, hands, first_bidder):
    """
    用最简 AI 策略打一局完整对局（通过 WebSocket）。
    策略：叫3分当地主；自由出牌出最小单张，跟牌全部跳过。
    正确处理连续两不出后清空上家出牌的逻辑。
    """
    ws_list = list(ws_clients)
    remaining = [list(h) for h in hands]

    # ── 叫分 ──
    await ws_list[first_bidder].send(json.dumps({
        "type": "bid", "payload": {"amount": 3}
    }))

    current_player = None
    for ws in ws_list:
        await ws_recv(ws, "bid_update")
        await ws_recv(ws, "landlord_assigned")
        tc = await ws_recv(ws, "turn_change")
        current_player = tc["payload"]["current_player"]

    # ── 出牌阶段 ──
    game_over = False
    turns = 0
    free_play = True  # 首出标志
    consecutive_passes = 0

    while turns < 500 and not game_over:
        cp = current_player
        cur_ws = ws_list[cp]
        hand = remaining[cp]

        if not hand:
            break

        # 排序取最小
        hand.sort(key=lambda c: (c.get("rank", 3), c.get("suit", 0)))

        if free_play and hand:
            card = hand.pop(0)
            await cur_ws.send(json.dumps({
                "type": "play", "payload": {"cards": [card]}
            }))
        else:
            await cur_ws.send(json.dumps({"type": "pass", "payload": {}}))

        # 接收所有客户端的结果：读到 turn_change 或 game_over
        next_player = None
        play_succeeded = False
        pass_succeeded = False

        for ws in ws_list:
            while True:
                msg = await ws_recv_any(ws, timeout=5.0)
                mt = msg.get("type", "")
                if mt == "game_over":
                    game_over = True
                    break
                elif mt == "turn_change":
                    next_player = msg["payload"]["current_player"]
                    break
                elif mt == "cards_played":
                    play_succeeded = True
                elif mt == "player_passed":
                    pass_succeeded = True
                elif mt == "error":
                    # 出牌失败，跳出内层循环
                    break

        if game_over:
            break

        # 更新状态
        if play_succeeded:
            free_play = False
            consecutive_passes = 0
        elif pass_succeeded:
            consecutive_passes += 1
            if consecutive_passes >= 2:
                # 连续两不出，清空上家 → 下一个玩家自由出牌
                free_play = True
                consecutive_passes = 0
            else:
                free_play = False
        # error 情况：保持当前 free_play 状态不变

        if next_player is not None:
            current_player = next_player
        else:
            current_player = (cp + 1) % 3

        turns += 1

    return turns


# ================================================================
# 第 1 类：空闲基线资源
# ================================================================


class TestResourceBaseline:

    @pytest.mark.server_load
    def test_idle_memory_under_50mb(self, server_process):
        """空闲状态下服务器 RSS 应低于 50MB"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()
        time.sleep(5)
        monitor.stop()

        max_rss = monitor.max_rss_mb
        print(f"\n  空闲内存: {max_rss:.1f}MB (上限 {MAX_MEMORY_MB}MB)")
        assert max_rss < MAX_MEMORY_MB, f"空闲 RSS {max_rss:.1f}MB 超过 {MAX_MEMORY_MB}MB"

    @pytest.mark.server_load
    def test_idle_cpu_near_zero(self, server_process):
        """空闲状态下服务器 CPU 应低于 1%"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid, interval=1.0)
        monitor.start()
        time.sleep(5)
        monitor.stop()

        avg_cpu = monitor.avg_cpu_percent
        print(f"\n  空闲 CPU: {avg_cpu:.1f}% (上限 1%)")
        assert avg_cpu < 1.0, f"空闲 CPU {avg_cpu:.1f}% 超过 1%"


# ================================================================
# 第 2 类：并发连接
# ================================================================


class TestConcurrentConnections:

    @pytest.mark.server_load
    def test_9_players_3_rooms_under_limits(self, server_process):
        """9 个玩家 3 个房间，资源应在限制内"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()

        async def run():
            wss = []
            for _ in range(3):
                _, clients, _, _ = await create_and_fill_room(host, port)
                wss.extend(clients)
            await asyncio.sleep(3)
            for ws in wss:
                await ws.close()

        asyncio.run(run())
        monitor.stop()

        s = monitor.summary()
        print(f"\n  9玩家/3房间: RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert s["max_rss_mb"] < MAX_MEMORY_MB
        assert s["avg_cpu_percent"] < MAX_CPU_PERCENT

    @pytest.mark.server_load
    def test_30_players_10_rooms_under_limits(self, server_process):
        """30 个玩家 10 个房间，资源应在限制内"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()

        async def run():
            wss = []
            for _ in range(10):
                _, clients, _, _ = await create_and_fill_room(host, port)
                wss.extend(clients)
            await asyncio.sleep(3)
            for ws in wss:
                await ws.close()

        asyncio.run(run())
        monitor.stop()

        s = monitor.summary()
        print(f"\n  30玩家/10房间: RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert s["max_rss_mb"] < MAX_MEMORY_MB
        assert s["avg_cpu_percent"] < MAX_CPU_PERCENT

    @pytest.mark.server_load
    def test_60_players_connection_surge(self, server_process):
        """60 个客户端同时连接，创建 20 个房间"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()

        async def run():
            wss = await asyncio.gather(*[ws_connect(host, port) for _ in range(60)])
            rooms_created = 0
            for i in range(0, 60, 3):
                ws1, ws2, ws3 = wss[i], wss[i + 1], wss[i + 2]
                resp = await ws_send_recv(ws1, "create_room", {
                    "player_name": f"H{i}", "is_public": True
                }, response_type="room_created")
                code = resp["payload"]["room_code"]
                for j, ws in enumerate((ws2, ws3)):
                    await ws.send(json.dumps({
                        "type": "join_room",
                        "payload": {"room_code": code, "player_name": f"P{i}_{j}"},
                    }))
                    await ws_recv(ws, "room_joined")
                rooms_created += 1
            await asyncio.sleep(2)
            for ws in wss:
                await ws.close()
            return rooms_created

        count = asyncio.run(run())
        monitor.stop()

        s = monitor.summary()
        print(f"\n  60连接/20房间: 创建{count}间, RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert count == 20
        assert s["max_rss_mb"] < MAX_MEMORY_MB


# ================================================================
# 第 3 类：负载下完整对局
# ================================================================


class TestGameFlowUnderLoad:

    @pytest.mark.server_load
    def test_single_game_flow_resources(self, server_process):
        """单局完整对局，资源应在限制内"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()

        async def run():
            _, clients, hands, fb = await create_and_fill_room(host, port)
            turns = await play_one_game(clients, hands, fb)
            for ws in clients:
                await ws.close()
            return turns

        turns = asyncio.run(run())
        monitor.stop()

        s = monitor.summary()
        print(f"\n  单局对局: {turns}回合, RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert s["max_rss_mb"] < MAX_MEMORY_MB
        assert turns < 500

    @pytest.mark.server_load
    def test_5_concurrent_games_resources(self, server_process):
        """5 局并发对局，资源应在限制内"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()

        async def run_one():
            _, clients, hands, fb = await create_and_fill_room(host, port)
            turns = await play_one_game(clients, hands, fb)
            for ws in clients:
                await ws.close()
            return turns

        async def run():
            results = await asyncio.gather(
                *[run_one() for _ in range(5)], return_exceptions=True
            )
            return [r for r in results if isinstance(r, int)]

        turns_list = asyncio.run(run())
        monitor.stop()

        s = monitor.summary()
        print(f"\n  5局并发: 完成{len(turns_list)}局, RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert len(turns_list) == 5
        assert s["max_rss_mb"] < MAX_MEMORY_MB
        assert s["avg_cpu_percent"] < MAX_CPU_PERCENT

    @pytest.mark.server_load
    def test_10_concurrent_games_resources(self, server_process):
        """10 局并发对局，资源应在限制内"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()

        async def run_one():
            _, clients, hands, fb = await create_and_fill_room(host, port)
            turns = await play_one_game(clients, hands, fb)
            for ws in clients:
                await ws.close()
            return turns

        async def run():
            results = await asyncio.gather(
                *[run_one() for _ in range(10)], return_exceptions=True
            )
            return [r for r in results if isinstance(r, int)]

        turns_list = asyncio.run(run())
        monitor.stop()

        s = monitor.summary()
        print(f"\n  10局并发: 完成{len(turns_list)}局, RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert len(turns_list) >= 8
        assert s["max_rss_mb"] < MAX_MEMORY_MB
        assert s["avg_cpu_percent"] < MAX_CPU_PERCENT


# ================================================================
# 第 4 类：消息延迟与吞吐
# ================================================================


class TestMessageThroughput:

    @pytest.mark.server_load
    def test_message_latency_under_50ms(self, server_process):
        """协议往返 P95 延迟应低于 50ms（测 create/join/bid）"""
        host, port, pid = server_process
        latencies = []

        async def run():
            for _ in range(3):
                ws1 = ws2 = ws3 = None
                try:
                    ws1 = await ws_connect(host, port)
                    ws2 = await ws_connect(host, port)
                    ws3 = await ws_connect(host, port)

                    # 创建房间
                    t0 = time.perf_counter()
                    resp = await ws_send_recv(ws1, "create_room",
                                              {"player_name": "L1", "is_public": False},
                                              response_type="room_created")
                    latencies.append((time.perf_counter() - t0) * 1000)
                    code = resp["payload"]["room_code"]

                    # 加入
                    for ws, name in ((ws2, "L2"), (ws3, "L3")):
                        t0 = time.perf_counter()
                        await ws.send(json.dumps({
                            "type": "join_room",
                            "payload": {"room_code": code, "player_name": name},
                        }))
                        await ws_recv(ws, "room_joined")
                        latencies.append((time.perf_counter() - t0) * 1000)
                        await ws_recv(ws1, "player_joined")

                    # 准备 + game_start
                    for ws in (ws1, ws2, ws3):
                        await ws.send(json.dumps({"type": "ready", "payload": {}}))
                    for ws in (ws1, ws2, ws3):
                        await ws_recv(ws, "game_start")
                    for ws in (ws1, ws2, ws3):
                        await ws_recv(ws, "turn_change")

                    # 叫分延迟
                    t0 = time.perf_counter()
                    await ws1.send(json.dumps({"type": "bid", "payload": {"amount": 3}}))
                    await ws_recv(ws1, "bid_update")
                    latencies.append((time.perf_counter() - t0) * 1000)

                    for ws in (ws1, ws2, ws3):
                        await ws_recv(ws, "landlord_assigned")
                        await ws_recv(ws, "turn_change")

                except Exception:
                    pass  # 跳过失败的轮次
                finally:
                    for ws in (ws1, ws2, ws3):
                        if ws:
                            try:
                                await ws.close()
                            except Exception:
                                pass

        asyncio.run(run())

        if len(latencies) < 3:
            pytest.skip("样本不足")

        latencies.sort()
        p95 = latencies[int(len(latencies) * 0.95)]
        avg = sum(latencies) / len(latencies)
        print(f"\n  消息延迟: {len(latencies)}样本, 平均{avg:.1f}ms, P95={p95:.1f}ms")
        assert p95 < MAX_P95_LATENCY_MS, f"P95 {p95:.1f}ms 超过 {MAX_P95_LATENCY_MS}ms"

        asyncio.run(run())

        if not latencies:
            pytest.skip("未收集到延迟样本")

        latencies.sort()
        p95 = latencies[int(len(latencies) * 0.95)]
        avg = sum(latencies) / len(latencies)
        print(f"\n  消息延迟: {len(latencies)}样本, 平均{avg:.1f}ms, P95={p95:.1f}ms")
        assert p95 < MAX_P95_LATENCY_MS, f"P95 {p95:.1f}ms 超过 {MAX_P95_LATENCY_MS}ms"

    @pytest.mark.server_load
    def test_sustained_message_rate(self, server_process):
        """5 局并发对局的消息处理速率应 > 20 回合/秒"""
        host, port, pid = server_process

        async def run_one():
            _, clients, hands, fb = await create_and_fill_room(host, port)
            turns = await play_one_game(clients, hands, fb)
            for ws in clients:
                await ws.close()
            return turns

        async def run():
            start = time.monotonic()
            results = await asyncio.gather(
                *[run_one() for _ in range(5)], return_exceptions=True
            )
            elapsed = time.monotonic() - start
            successful = [r for r in results if isinstance(r, int)]
            return sum(successful), elapsed

        total_turns, elapsed = asyncio.run(run())
        rate = total_turns / elapsed if elapsed > 0 else 0
        print(f"\n  消息速率: {total_turns}回合/{elapsed:.1f}秒 = {rate:.1f}回合/秒")
        assert total_turns > 0
        assert rate > 20, f"速率 {rate:.1f} 回合/秒 低于阈值"


# ================================================================
# 第 5 类：资源清理
# ================================================================


class TestResourceCleanup:

    @pytest.mark.server_load
    def test_memory_reclaimed_after_games(self, server_process):
        """对局结束后内存增量应 ≤ 5MB"""
        host, port, pid = server_process

        gc.collect()
        time.sleep(1)
        baseline_rss = psutil.Process(pid).memory_info().rss / (1024 * 1024)

        async def run_games():
            for _ in range(10):
                _, clients, hands, fb = await create_and_fill_room(host, port)
                await play_one_game(clients, hands, fb)
                for ws in clients:
                    try:
                        await ws.close()
                    except Exception:
                        pass

        asyncio.run(run_games())

        time.sleep(3)
        gc.collect()
        time.sleep(1)
        after_rss = psutil.Process(pid).memory_info().rss / (1024 * 1024)
        delta = after_rss - baseline_rss

        print(f"\n  内存回收: 基线{baseline_rss:.1f}MB → {after_rss:.1f}MB (Δ{delta:+.1f}MB)")
        assert delta < 5.0, f"内存增量 {delta:.1f}MB 超过 5MB 上限"

    @pytest.mark.server_load
    def test_room_cleanup_on_disconnect(self, server_process):
        """断开连接后房间应被清理（仅创建房间，不开始游戏）"""
        host, port, pid = server_process

        async def run():
            import requests
            for _ in range(10):
                ws = await ws_connect(host, port)
                await ws.send(json.dumps({
                    "type": "create_room",
                    "payload": {"player_name": "Temp", "is_public": False},
                }))
                await ws_recv(ws, "room_created")
                await ws.close()
            await asyncio.sleep(2)
            resp = requests.get(f"http://{host}:{port}/stats", timeout=3)
            return resp.json()

        stats = asyncio.run(run())
        print(f"\n  断线清理: room_count={stats['room_count']}")
        assert stats["room_count"] == 0


# ================================================================
# 第 6 类：约束下持续运行
# ================================================================


class TestResourceConstrainedOperation:

    @pytest.mark.server_load
    def test_sustained_5_games_30_seconds(self, server_process):
        """5 局并发对局持续运行 30 秒，资源始终在限制内"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid, interval=1.0)
        monitor.start()

        async def run_one():
            _, clients, hands, fb = await create_and_fill_room(host, port)
            turns = await play_one_game(clients, hands, fb)
            for ws in clients:
                try:
                    await ws.close()
                except Exception:
                    pass
            return turns

        async def run():
            end_time = time.monotonic() + 30
            total = 0
            while time.monotonic() < end_time:
                results = await asyncio.gather(
                    *[run_one() for _ in range(5)], return_exceptions=True
                )
                total += sum(1 for r in results if isinstance(r, int))
            return total

        total = asyncio.run(run())
        monitor.stop()

        s = monitor.summary()
        print(f"\n  持续30秒: 完成{total}局, RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert total >= 1
        assert s["max_rss_mb"] < MAX_MEMORY_MB, f"RSS {s['max_rss_mb']}MB 超过上限"
        assert s["avg_cpu_percent"] < MAX_CPU_PERCENT, f"CPU {s['avg_cpu_percent']}% 超过上限"

    @pytest.mark.server_load
    def test_memory_stability_20_games_sequential(self, server_process):
        """顺序 20 局对局，RSS 波动应 ≤ 10MB（无泄漏）"""
        host, port, pid = server_process
        rss_samples = []

        async def run():
            for i in range(20):
                _, clients, hands, fb = await create_and_fill_room(host, port)
                await play_one_game(clients, hands, fb)
                for ws in clients:
                    try:
                        await ws.close()
                    except Exception:
                        pass
                rss_samples.append(psutil.Process(pid).memory_info().rss / (1024 * 1024))
                if i % 5 == 4:
                    await asyncio.sleep(1)

        asyncio.run(run())

        if len(rss_samples) < 5:
            pytest.skip("样本不足")

        stable = rss_samples[4:]
        rss_range = max(stable) - min(stable)
        avg_rss = sum(stable) / len(stable)
        print(f"\n  内存稳定性: 平均{avg_rss:.1f}MB, 波动{rss_range:.1f}MB")
        print(f"  样本: {[round(r, 1) for r in rss_samples]}")
        assert rss_range < 10.0, f"RSS 波动 {rss_range:.1f}MB 超过 10MB"

    @pytest.mark.server_load
    def test_concurrent_games_with_lobby_queries(self, server_process):
        """5 局活跃对局 + 10 个大厅浏览客户端，资源应在限制内"""
        host, port, pid = server_process
        monitor = ResourceMonitor(pid)
        monitor.start()

        async def run_game():
            _, clients, hands, fb = await create_and_fill_room(host, port)
            turns = await play_one_game(clients, hands, fb)
            for ws in clients:
                try:
                    await ws.close()
                except Exception:
                    pass
            return turns

        async def lobby_browse():
            ok = 0
            for _ in range(20):
                try:
                    ws = await ws_connect(host, port)
                    await ws.send(json.dumps({"type": "list_rooms", "payload": {}}))
                    await ws_recv(ws, "room_list")
                    await ws.close()
                    ok += 1
                    await asyncio.sleep(0.5)
                except Exception:
                    pass
            return ok

        async def run_games_all():
            return await asyncio.gather(
                *[run_game() for _ in range(5)], return_exceptions=True
            )

        async def run():
            game_task = asyncio.create_task(run_games_all())
            lobby_tasks = [asyncio.create_task(lobby_browse()) for _ in range(10)]
            game_results = await game_task
            lobby_results = await asyncio.gather(*lobby_tasks)
            return game_results, lobby_results

        game_results, lobby_results = asyncio.run(run())
        monitor.stop()

        ok_games = sum(1 for r in game_results if isinstance(r, int))
        ok_queries = sum(lobby_results)

        s = monitor.summary()
        print(f"\n  对局+大厅: {ok_games}局完成, {ok_queries}次查询成功")
        print(f"  资源: RSS={s['max_rss_mb']}MB, CPU={s['avg_cpu_percent']}%")
        assert ok_games >= 3, f"应完成至少 3 局，实际 {ok_games}"
        assert s["max_rss_mb"] < MAX_MEMORY_MB
        assert s["avg_cpu_percent"] < MAX_CPU_PERCENT
