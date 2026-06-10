"""
test_master_slave.py - 主从服务器测试
测试列表服务器（主）和从服务器的注册、心跳、列表、启停功能
"""

import sys
import os
import time
import json
import asyncio
import subprocess
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import psutil
import pytest
import requests
import websockets
from tests.conftest import wait_for_server_in_list, wait_until

SERVER_DIR = Path(__file__).resolve().parent.parent
HOST = "127.0.0.1"

# 端口分配器
_master_port = 19000
_slave_port = 19100


def _master_servers(host, port):
    r = requests.get(f"http://{host}:{port}/api/servers", timeout=3)
    r.raise_for_status()
    return r.json()["servers"]


def _find_slave_server(master_host, master_port, slave_port, timeout=10.0):
    return wait_for_server_in_list(
        lambda: _master_servers(master_host, master_port),
        slave_port,
        timeout=timeout,
    )


@pytest.fixture()
def master_server():
    """启动列表服务器（无 API Key，测试管理端点免密）"""
    global _master_port
    port = _master_port
    _master_port += 1

    # 临时清除 API Key
    config_file = SERVER_DIR / "master_config.json"
    original = config_file.read_text(encoding="utf-8") if config_file.exists() else None
    config_file.write_text(json.dumps({
        "port": port, "cleanup_interval": 30, "dead_timeout": 60, "api_key": "",
    }, ensure_ascii=False, indent=2), encoding="utf-8")

    env = os.environ.copy()
    env["MASTER_PORT"] = str(port)
    proc = subprocess.Popen(
        [sys.executable, "master.py"],
        cwd=str(SERVER_DIR),
        env=env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    try:
        for _ in range(20):
            if proc.poll() is not None:
                raise RuntimeError(f"列表服务器已退出: {proc.returncode}")
            try:
                r = requests.get(f"http://{HOST}:{port}/health", timeout=1)
                if r.status_code == 200:
                    break
            except Exception:
                pass
            time.sleep(0.5)
        else:
            proc.kill()
            raise RuntimeError("列表服务器 10 秒内未就绪")

        yield HOST, port, proc.pid
    finally:
        proc.terminate()
        proc.wait(timeout=5)
        if original:
            config_file.write_text(original, encoding="utf-8")


@pytest.fixture()
def slave_server(master_server):
    """启动从服务器，注册到列表服务器"""
    global _slave_port
    m_host, m_port, _ = master_server
    s_port = _slave_port
    _slave_port += 1

    # 提高并发对局上限
    config_file = SERVER_DIR / "server_config.json"
    original_config = None
    if config_file.exists():
        original_config = config_file.read_text(encoding="utf-8")
    config_file.write_text('{"max_concurrent_games": 100}', encoding="utf-8")

    env = os.environ.copy()
    env["SERVER_PORT"] = str(s_port)
    env["SLAVE_PORT"] = str(s_port)
    env["MASTER_URL"] = f"ws://{m_host}:{m_port}/ws/slave"
    env["SLAVE_NAME"] = f"测试服务器_{s_port}"
    env["SLAVE_HOST"] = m_host

    proc = subprocess.Popen(
        [sys.executable, "main.py"],
        cwd=str(SERVER_DIR),
        env=env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    try:
        for _ in range(20):
            if proc.poll() is not None:
                raise RuntimeError(f"从服务器已退出: {proc.returncode}")
            try:
                r = requests.get(f"http://{HOST}:{s_port}/health", timeout=1)
                if r.status_code == 200:
                    break
            except Exception:
                pass
            time.sleep(0.5)
        else:
            proc.kill()
            raise RuntimeError("从服务器 10 秒内未就绪")

        _find_slave_server(m_host, m_port, s_port)

        yield HOST, s_port, proc.pid
    finally:
        proc.terminate()
        proc.wait(timeout=5)
        if original_config is not None:
            config_file.write_text(original_config, encoding="utf-8")


# ================================================================
# 列表服务器基础测试
# ================================================================


class TestMasterServer:

    @pytest.mark.server_load
    def test_master_health(self, master_server):
        """列表服务器健康检查"""
        host, port, pid = master_server
        r = requests.get(f"http://{host}:{port}/health", timeout=3)
        assert r.status_code == 200
        assert r.json()["status"] == "ok"

    @pytest.mark.server_load
    def test_master_root(self, master_server):
        """列表服务器根端点"""
        host, port, pid = master_server
        r = requests.get(f"http://{host}:{port}/", timeout=3)
        assert r.status_code == 200
        data = r.json()
        assert "列表服务器" in data["message"]
        assert data["server_count"] == 0

    @pytest.mark.server_load
    def test_master_empty_server_list(self, master_server):
        """无从服务器时列表为空"""
        host, port, pid = master_server
        r = requests.get(f"http://{host}:{port}/api/servers", timeout=3)
        assert r.status_code == 200
        assert r.json()["servers"] == []


# ================================================================
# 从服务器注册测试
# ================================================================


class TestSlaveRegistration:

    @pytest.mark.server_load
    def test_slave_registers_with_master(self, slave_server):
        """从服务器启动后自动注册到列表服务器"""
        s_host, s_port, _ = slave_server
        # 从 slave_server fixture 获取 master 端口（通过检查 master 的 /api/servers）
        # 这里直接检查从服务器本身是健康的
        r = requests.get(f"http://{s_host}:{s_port}/health", timeout=3)
        assert r.status_code == 200

    @pytest.mark.server_load
    def test_slave_appears_in_master_list(self, master_server, slave_server):
        """从服务器出现在列表服务器的列表中"""
        m_host, m_port, _ = master_server
        s_host, s_port, _ = slave_server

        server = _find_slave_server(m_host, m_port, s_port)
        assert server["port"] == s_port

    @pytest.mark.server_load
    def test_slave_heartbeat_updates_stats(self, master_server, slave_server):
        """心跳更新从服务器统计数据"""
        m_host, m_port, _ = master_server
        _, s_port, _ = slave_server

        server = _find_slave_server(m_host, m_port, s_port)
        assert "active_games" in server
        assert "connected_players" in server
        assert server["is_alive"] is True


# ================================================================
# 服务器启停测试
# ================================================================


class TestServerEnableDisable:

    @pytest.mark.server_load
    def test_disable_slave(self, master_server, slave_server):
        """禁用从服务器"""
        m_host, m_port, _ = master_server
        s_host, s_port, _ = slave_server

        server = _find_slave_server(m_host, m_port, s_port)
        sid = server["server_id"]

        # 禁用
        r = requests.post(f"http://{m_host}:{m_port}/api/servers/{sid}/disable", timeout=3)
        assert r.status_code == 200
        assert r.json()["enabled"] is False

        # 验证列表中显示为禁用
        r = requests.get(f"http://{m_host}:{m_port}/api/servers", timeout=3)
        servers = r.json()["servers"]
        server = next(s for s in servers if s["server_id"] == sid)
        assert server["enabled"] is False

    @pytest.mark.server_load
    def test_enable_slave(self, master_server, slave_server):
        """启用已禁用的从服务器"""
        m_host, m_port, _ = master_server
        _, s_port, _ = slave_server

        sid = _find_slave_server(m_host, m_port, s_port)["server_id"]

        # 先禁用
        requests.post(f"http://{m_host}:{m_port}/api/servers/{sid}/disable", timeout=3)
        # 再启用
        r = requests.post(f"http://{m_host}:{m_port}/api/servers/{sid}/enable", timeout=3)
        assert r.status_code == 200
        assert r.json()["enabled"] is True

    @pytest.mark.server_load
    def test_remove_slave(self, master_server, slave_server):
        """移除从服务器"""
        m_host, m_port, _ = master_server
        _, s_port, _ = slave_server

        sid = _find_slave_server(m_host, m_port, s_port)["server_id"]

        r = requests.delete(f"http://{m_host}:{m_port}/api/servers/{sid}", timeout=3)
        assert r.status_code == 200

        wait_until(
            lambda: all(s["server_id"] != sid for s in _master_servers(m_host, m_port)),
            timeout=5,
        )

    @pytest.mark.server_load
    def test_disable_nonexistent_returns_404(self, master_server):
        """操作不存在的从服务器返回 404"""
        m_host, m_port, _ = master_server
        r = requests.post(f"http://{m_host}:{m_port}/api/servers/nonexistent/disable", timeout=3)
        assert r.status_code == 404


# ================================================================
# 大厅 WebSocket 测试
# ================================================================


class TestLobbyWebSocket:

    @pytest.mark.server_load
    def test_lobby_receives_server_list(self, master_server, slave_server):
        """大厅客户端连接后立即收到服务器列表"""
        m_host, m_port, _ = master_server
        _, s_port, _ = slave_server
        _find_slave_server(m_host, m_port, s_port)

        async def run():
            ws = await websockets.connect(f"ws://{m_host}:{m_port}/ws/lobby")
            try:
                raw = await asyncio.wait_for(ws.recv(), timeout=5)
                msg = json.loads(raw)
                assert msg["type"] == "server_list"
                servers = msg["payload"]["servers"]
                assert any(s["port"] == s_port for s in servers)
                return servers
            finally:
                await ws.close()

        servers = asyncio.run(run())
        assert any("port" in s for s in servers)

    @pytest.mark.server_load
    def test_lobby_receives_update_on_register(self, master_server):
        """从服务器注册时大厅客户端收到推送"""
        m_host, m_port, _ = master_server

        async def run():
            ws = await websockets.connect(f"ws://{m_host}:{m_port}/ws/lobby")
            try:
                # 初始列表为空
                raw = await asyncio.wait_for(ws.recv(), timeout=5)
                msg = json.loads(raw)
                assert msg["type"] == "server_list"
                assert len(msg["payload"]["servers"]) == 0

                # 注册一个从服务器
                slave_ws = await websockets.connect(f"ws://{m_host}:{m_port}/ws/slave")
                await slave_ws.send(json.dumps({
                    "type": "register",
                    "payload": {
                        "server_name": "测试推送",
                        "host": m_host,
                        "port": 19999,
                    },
                }))
                await asyncio.wait_for(slave_ws.recv(), timeout=5)  # registered

                # 大厅客户端应收到更新
                raw = await asyncio.wait_for(ws.recv(), timeout=5)
                msg = json.loads(raw)
                assert msg["type"] == "server_list_updated"
                assert len(msg["payload"]["servers"]) >= 1

                await slave_ws.close()
            finally:
                await ws.close()

        asyncio.run(run())

    @pytest.mark.server_load
    def test_lobby_receives_update_on_disable(self, master_server, slave_server):
        """禁用从服务器时大厅客户端收到推送"""
        m_host, m_port, _ = master_server
        _, s_port, _ = slave_server

        async def run():
            ws = await websockets.connect(f"ws://{m_host}:{m_port}/ws/lobby")
            try:
                # 跳过初始列表
                await asyncio.wait_for(ws.recv(), timeout=5)

                sid = _find_slave_server(m_host, m_port, s_port)["server_id"]

                # 通过 HTTP 禁用
                requests.post(f"http://{m_host}:{m_port}/api/servers/{sid}/disable", timeout=3)

                # 收到推送
                raw = await asyncio.wait_for(ws.recv(), timeout=5)
                msg = json.loads(raw)
                assert msg["type"] == "server_list_updated"
                server = next(s for s in msg["payload"]["servers"] if s["server_id"] == sid)
                assert server["enabled"] is False
            finally:
                await ws.close()

        asyncio.run(run())


# ================================================================
# 端到端：客户端通过列表服务器选择从服务器
# ================================================================


class TestEndToEnd:

    @pytest.mark.server_load
    def test_client_browses_then_connects_to_slave(self, master_server, slave_server):
        """客户端浏览列表 → 选择从服务器 → 直接连接并创建房间"""
        m_host, m_port, _ = master_server
        s_host, s_port, _ = slave_server
        _find_slave_server(m_host, m_port, s_port)

        async def run():
            # 1. 连接大厅获取服务器列表
            lobby_ws = await websockets.connect(f"ws://{m_host}:{m_port}/ws/lobby")
            raw = await asyncio.wait_for(lobby_ws.recv(), timeout=5)
            msg = json.loads(raw)
            servers = msg["payload"]["servers"]
            target = next(s for s in servers if s["port"] == s_port)
            await lobby_ws.close()

            # 2. 直接连接到选中的从服务器
            game_ws = await websockets.connect(target["ws_url"])
            await game_ws.send(json.dumps({
                "type": "create_room",
                "payload": {"player_name": "测试玩家", "is_public": False},
            }))
            raw = await asyncio.wait_for(game_ws.recv(), timeout=5)
            msg = json.loads(raw)
            assert msg["type"] == "room_created"
            assert "room_code" in msg["payload"]

            await game_ws.close()
            return msg["payload"]["room_code"]

        room_code = asyncio.run(run())
        assert len(room_code) == 6


# ================================================================
# API Key 验证测试
# ================================================================


@pytest.fixture()
def master_with_api_key():
    """启动配置了 API Key 的列表服务器"""
    global _master_port
    port = _master_port
    _master_port += 1

    # 写入带 api_key 的配置
    config_file = SERVER_DIR / "master_config.json"
    original = config_file.read_text(encoding="utf-8") if config_file.exists() else None
    config_file.write_text(json.dumps({
        "port": port,
        "cleanup_interval": 30,
        "dead_timeout": 60,
        "api_key": "test-secret-key-12345",
    }, ensure_ascii=False, indent=2), encoding="utf-8")

    env = os.environ.copy()
    env["MASTER_PORT"] = str(port)
    proc = subprocess.Popen(
        [sys.executable, "master.py"],
        cwd=str(SERVER_DIR),
        env=env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0,
    )
    try:
        for _ in range(20):
            if proc.poll() is not None:
                raise RuntimeError(f"列表服务器已退出: {proc.returncode}")
            try:
                r = requests.get(f"http://{HOST}:{port}/health", timeout=1)
                if r.status_code == 200:
                    break
            except Exception:
                pass
            time.sleep(0.5)
        else:
            proc.kill()
            raise RuntimeError("列表服务器 10 秒内未就绪")

        yield HOST, port, proc.pid, "test-secret-key-12345"
    finally:
        proc.terminate()
        proc.wait(timeout=5)
        if original:
            config_file.write_text(original, encoding="utf-8")


class TestApiKeyAuth:

    @pytest.mark.server_load
    def test_management_requires_api_key(self, master_with_api_key):
        """管理接口在配置了密钥后需要 X-API-Key 头"""
        host, port, pid, key = master_with_api_key

        async def run():
            # 注册从服务器（不需要密钥）
            ws = await websockets.connect(f"ws://{host}:{port}/ws/slave")
            await ws.send(json.dumps({
                "type": "register",
                "payload": {"server_name": "测试", "host": host, "port": 19999},
            }))
            await asyncio.wait_for(ws.recv(), timeout=5)

            # 获取 server_id
            r = requests.get(f"http://{host}:{port}/api/servers", timeout=3)
            servers = r.json()["servers"]
            assert len(servers) >= 1
            sid = servers[0]["server_id"]

            # 无密钥 → 401
            r = requests.post(f"http://{host}:{port}/api/servers/{sid}/disable", timeout=3)
            assert r.status_code == 401

            # 错误密钥 → 401
            r = requests.post(f"http://{host}:{port}/api/servers/{sid}/disable",
                              headers={"X-API-Key": "wrong-key"}, timeout=3)
            assert r.status_code == 401

            # 正确密钥 → 200
            r = requests.post(f"http://{host}:{port}/api/servers/{sid}/disable",
                              headers={"X-API-Key": key}, timeout=3)
            assert r.status_code == 200
            assert r.json()["enabled"] is False

            await ws.close()

        asyncio.run(run())

    @pytest.mark.server_load
    def test_read_endpoints_no_key_required(self, master_with_api_key):
        """读取接口（列表、健康检查）不需要密钥"""
        host, port, pid, key = master_with_api_key

        r = requests.get(f"http://{host}:{port}/api/servers", timeout=3)
        assert r.status_code == 200
        r = requests.get(f"http://{host}:{port}/health", timeout=3)
        assert r.status_code == 200
        r = requests.get(f"http://{host}:{port}/", timeout=3)
        assert r.status_code == 200

    @pytest.mark.server_load
    def test_enable_requires_api_key(self, master_with_api_key):
        """启用接口也需要密钥"""
        host, port, pid, key = master_with_api_key

        async def run():
            ws = await websockets.connect(f"ws://{host}:{port}/ws/slave")
            await ws.send(json.dumps({
                "type": "register",
                "payload": {"server_name": "测试2", "host": host, "port": 19998},
            }))
            await asyncio.wait_for(ws.recv(), timeout=5)

            r = requests.get(f"http://{host}:{port}/api/servers", timeout=3)
            sid = r.json()["servers"][0]["server_id"]

            # 先禁用
            r = requests.post(f"http://{host}:{port}/api/servers/{sid}/disable",
                              headers={"X-API-Key": key}, timeout=3)
            assert r.status_code == 200

            # 无密钥启用 → 401
            r = requests.post(f"http://{host}:{port}/api/servers/{sid}/enable", timeout=3)
            assert r.status_code == 401

            # 有密钥启用 → 200
            r = requests.post(f"http://{host}:{port}/api/servers/{sid}/enable",
                              headers={"X-API-Key": key}, timeout=3)
            assert r.status_code == 200
            assert r.json()["enabled"] is True

            await ws.close()

        asyncio.run(run())

    @pytest.mark.server_load
    def test_remove_requires_api_key(self, master_with_api_key):
        """移除接口也需要密钥"""
        host, port, pid, key = master_with_api_key

        async def run():
            ws = await websockets.connect(f"ws://{host}:{port}/ws/slave")
            await ws.send(json.dumps({
                "type": "register",
                "payload": {"server_name": "测试3", "host": host, "port": 19997},
            }))
            await asyncio.wait_for(ws.recv(), timeout=5)

            r = requests.get(f"http://{host}:{port}/api/servers", timeout=3)
            sid = r.json()["servers"][0]["server_id"]

            # 无密钥移除 → 401
            r = requests.delete(f"http://{host}:{port}/api/servers/{sid}", timeout=3)
            assert r.status_code == 401

            # 有密钥移除 → 200
            r = requests.delete(f"http://{host}:{port}/api/servers/{sid}",
                                headers={"X-API-Key": key}, timeout=3)
            assert r.status_code == 200

            await ws.close()

        asyncio.run(run())
