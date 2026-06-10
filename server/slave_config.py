"""
slave_config.py - 从服务器注册客户端
负责向列表服务器注册、发送心跳、接收启停指令
"""

import asyncio
import json
import logging
import time
from dataclasses import dataclass, field
from typing import Callable, Optional

import websockets

logger = logging.getLogger(__name__)


@dataclass
class SlaveConfig:
    """从服务器注册配置"""
    server_id: str = ""
    server_name: str = "萝莉丝扑克服务器"
    host: str = "127.0.0.1"
    port: int = 8050
    max_concurrent_games: int = 10
    # 运行时状态（由外部回调提供）
    active_games: int = 0
    connected_players: int = 0
    room_count: int = 0
    is_enabled: bool = True


class SlaveRegistration:
    """
    从服务器注册客户端
    连接列表服务器，注册自身，周期性发送心跳
    """

    def __init__(
        self,
        master_url: str,
        config: SlaveConfig,
        on_enabled_changed: Optional[Callable[[bool], None]] = None,
        get_stats: Optional[Callable[[], dict]] = None,
    ):
        self.master_url = master_url
        self.config = config
        self.on_enabled_changed = on_enabled_changed
        self.get_stats = get_stats

        self._ws: Optional[object] = None
        self._running = False
        self._task: Optional[asyncio.Task] = None

    async def start(self):
        """启动注册（后台任务）"""
        if self._running:
            return
        self._running = True
        self._task = asyncio.create_task(self._run_loop())
        logger.info(f"从服务器注册启动: master={self.master_url}")

    async def stop(self):
        """停止注册"""
        self._running = False
        if self._task:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
        if self._ws:
            try:
                await self._ws.close()
            except Exception:
                pass
            self._ws = None
        logger.info("从服务器注册已停止")

    async def update_stats(self, active_games: int, connected_players: int, room_count: int):
        """更新运行时统计（由外部调用）"""
        self.config.active_games = active_games
        self.config.connected_players = connected_players
        self.config.room_count = room_count

    async def _run_loop(self):
        """主循环：连接 → 注册 → 心跳 → 断线重连"""
        while self._running:
            try:
                await self._connect_and_run()
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.warning(f"与列表服务器断开: {e}，5秒后重连")
                await asyncio.sleep(5)

    async def _connect_and_run(self):
        """连接列表服务器并维持心跳"""
        async with websockets.connect(self.master_url) as ws:
            self._ws = ws
            logger.info(f"已连接列表服务器: {self.master_url}")

            # 发送注册
            await ws.send(json.dumps({
                "type": "register",
                "payload": self._make_register_payload(),
            }))

            # 等待注册确认
            raw = await asyncio.wait_for(ws.recv(), timeout=10)
            msg = json.loads(raw)
            if msg.get("type") == "registered":
                self.config.server_id = msg["payload"].get("server_id", self.config.server_id)
                self.config.is_enabled = msg["payload"].get("enabled", True)
                logger.info(f"注册成功: server_id={self.config.server_id}, enabled={self.config.is_enabled}")
            elif msg.get("type") == "error":
                logger.error(f"注册失败: {msg['payload'].get('message')}")
                return

            # 心跳 + 接收指令循环
            heartbeat_interval = 10  # 秒
            last_heartbeat = 0

            while self._running:
                try:
                    # 发送心跳
                    now = time.monotonic()
                    if now - last_heartbeat >= heartbeat_interval:
                        await self._send_heartbeat(ws)
                        last_heartbeat = now

                    # 非阻塞接收消息
                    try:
                        raw = await asyncio.wait_for(ws.recv(), timeout=1.0)
                        msg = json.loads(raw)
                        await self._handle_master_message(msg)
                    except asyncio.TimeoutError:
                        pass  # 没有消息，继续心跳循环

                except websockets.exceptions.ConnectionClosed:
                    logger.warning("与列表服务器的连接已断开")
                    break

    async def _send_heartbeat(self, ws):
        """发送心跳"""
        stats = {}
        if self.get_stats:
            stats = self.get_stats()
            self.config.room_count = stats.get("room_count", self.config.room_count)
            self.config.connected_players = stats.get("connected_players", self.config.connected_players)

        await ws.send(json.dumps({
            "type": "heartbeat",
            "payload": {
                "active_games": self.config.room_count,  # 向后兼容：active_games 现在代表房间数
                "connected_players": self.config.connected_players,
                "room_count": self.config.room_count,
            },
        }))

    async def _handle_master_message(self, msg: dict):
        """处理列表服务器发来的指令"""
        msg_type = msg.get("type")
        payload = msg.get("payload", {})

        if msg_type == "set_enabled":
            enabled = payload.get("enabled", True)
            self.config.is_enabled = enabled
            logger.info(f"收到启停指令: enabled={enabled}")
            if self.on_enabled_changed:
                self.on_enabled_changed(enabled)

        elif msg_type == "ping":
            if self._ws:
                await self._ws.send(json.dumps({"type": "pong"}))

        else:
            logger.warning(f"未知主服务器消息: {msg_type}")

    def _make_register_payload(self) -> dict:
        return {
            "server_id": self.config.server_id,
            "server_name": self.config.server_name,
            "host": self.config.host,
            "port": self.config.port,
            "max_concurrent_games": self.config.max_concurrent_games,
            "active_games": self.config.active_games,
            "connected_players": self.config.connected_players,
        }
