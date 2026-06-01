"""
master.py - 列表服务器（主服务器）
管理从服务器注册、提供服务器列表、启停从服务器
不涉及任何游戏逻辑，仅做服务器发现和管理
"""

import asyncio
import json
import logging
import time
from dataclasses import dataclass, field
from typing import Optional
from uuid import uuid4

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.responses import JSONResponse

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger(__name__)

app = FastAPI(title="萝莉丝扑克 - 列表服务器", version="1.0.0")


# ================================================================
# 数据模型
# ================================================================


@dataclass
class SlaveServerInfo:
    """从服务器信息"""
    server_id: str
    server_name: str
    host: str
    port: int
    max_concurrent_games: int = 10
    active_games: int = 0
    connected_players: int = 0
    room_count: int = 0
    enabled: bool = True
    registered_at: float = field(default_factory=time.time)
    last_heartbeat: float = field(default_factory=time.time)
    # WebSocket 连接（用于发送指令）
    websocket: Optional[WebSocket] = field(default=None, repr=False)

    @property
    def ws_url(self) -> str:
        """客户端连接地址"""
        return f"ws://{self.host}:{self.port}/ws"

    @property
    def is_alive(self) -> bool:
        """心跳是否存活（30秒超时）"""
        return time.time() - self.last_heartbeat < 30

    def to_public_dict(self) -> dict:
        """公开信息（给客户端浏览）"""
        return {
            "server_id": self.server_id,
            "server_name": self.server_name,
            "host": self.host,
            "port": self.port,
            "ws_url": self.ws_url,
            "max_concurrent_games": self.max_concurrent_games,
            "active_games": self.active_games,
            "connected_players": self.connected_players,
            "room_count": self.room_count,
            "enabled": self.enabled,
            "is_alive": self.is_alive,
        }


# ================================================================
# 服务器注册表
# ================================================================


class ServerRegistry:
    """从服务器注册表"""

    def __init__(self):
        self._servers: dict[str, SlaveServerInfo] = {}  # server_id -> info

    def register(self, info: SlaveServerInfo) -> SlaveServerInfo:
        """注册或更新从服务器"""
        existing = self._servers.get(info.server_id)
        if existing:
            # 更新已有记录（保留旧字段）
            existing.server_name = info.server_name
            existing.host = info.host
            existing.port = info.port
            existing.max_concurrent_games = info.max_concurrent_games
            existing.websocket = info.websocket
            existing.last_heartbeat = time.time()
            logger.info(f"从服务器更新: {info.server_id} ({info.server_name})")
            return existing
        else:
            self._servers[info.server_id] = info
            logger.info(f"从服务器注册: {info.server_id} ({info.server_name} @ {info.host}:{info.port})")
            return info

    def unregister(self, server_id: str):
        """注销从服务器"""
        if server_id in self._servers:
            del self._servers[server_id]
            logger.info(f"从服务器注销: {server_id}")

    def get_all(self) -> list[SlaveServerInfo]:
        """获取所有从服务器"""
        return list(self._servers.values())

    def get_alive(self) -> list[SlaveServerInfo]:
        """获取所有存活的从服务器"""
        return [s for s in self._servers.values() if s.is_alive]

    def get_public_list(self) -> list[dict]:
        """获取公开服务器列表（给客户端）"""
        return [s.to_public_dict() for s in self._servers.values() if s.is_alive]

    def get_by_id(self, server_id: str) -> Optional[SlaveServerInfo]:
        return self._servers.get(server_id)

    def update_heartbeat(self, server_id: str, stats: dict):
        """更新心跳和统计数据"""
        info = self._servers.get(server_id)
        if info:
            info.last_heartbeat = time.time()
            info.active_games = stats.get("active_games", info.active_games)
            info.connected_players = stats.get("connected_players", info.connected_players)
            info.room_count = stats.get("room_count", info.room_count)

    def cleanup_dead(self, timeout: float = 60):
        """清理超时的从服务器"""
        now = time.time()
        dead = [sid for sid, info in self._servers.items()
                if now - info.last_heartbeat > timeout]
        for sid in dead:
            del self._servers[sid]
            logger.info(f"清理超时从服务器: {sid}")


# 全局注册表
registry = ServerRegistry()

# 所有大厅 WebSocket 连接（用于推送更新）
lobby_connections: set[WebSocket] = set()


# ================================================================
# 从服务器注册端点 (WebSocket)
# ================================================================


@app.websocket("/ws/slave")
async def slave_websocket(websocket: WebSocket):
    """从服务器注册和心跳 WebSocket"""
    await websocket.accept()
    server_id = None

    try:
        # 等待注册消息
        raw = await asyncio.wait_for(websocket.receive_text(), timeout=10)
        msg = json.loads(raw)

        if msg.get("type") != "register":
            await websocket.send_json({
                "type": "error",
                "payload": {"message": "首条消息必须是 register"}
            })
            return

        payload = msg.get("payload", {})
        server_id = payload.get("server_id") or uuid4().hex[:8]

        info = SlaveServerInfo(
            server_id=server_id,
            server_name=payload.get("server_name", "未命名服务器"),
            host=payload.get("host", "127.0.0.1"),
            port=payload.get("port", 8050),
            max_concurrent_games=payload.get("max_concurrent_games", 10),
            active_games=payload.get("active_games", 0),
            connected_players=payload.get("connected_players", 0),
            websocket=websocket,
        )
        registry.register(info)

        # 返回注册确认
        await websocket.send_json({
            "type": "registered",
            "payload": {
                "server_id": server_id,
                "enabled": info.enabled,
            },
        })

        # 推送大厅更新
        await broadcast_lobby_update()

        # 心跳循环
        while True:
            raw = await websocket.receive_text()
            msg = json.loads(raw)

            if msg.get("type") == "heartbeat":
                registry.update_heartbeat(server_id, msg.get("payload", {}))
            elif msg.get("type") == "pong":
                pass
            else:
                logger.warning(f"从服务器 {server_id} 未知消息: {msg.get('type')}")

    except (WebSocketDisconnect, asyncio.CancelledError, Exception) as e:
        logger.info(f"从服务器 {server_id} 断开: {e}")
    finally:
        if server_id:
            registry.unregister(server_id)
            await broadcast_lobby_update()


# ================================================================
# 大厅 WebSocket 端点（客户端浏览服务器列表）
# ================================================================


@app.websocket("/ws/lobby")
async def lobby_websocket(websocket: WebSocket):
    """客户端大厅 WebSocket，接收服务器列表实时推送"""
    await websocket.accept()
    lobby_connections.add(websocket)

    try:
        # 立即推送当前列表
        await send_server_list(websocket)

        # 等待客户端请求
        while True:
            raw = await websocket.receive_text()
            msg = json.loads(raw)

            if msg.get("type") == "list_servers":
                await send_server_list(websocket)
            else:
                await websocket.send_json({
                    "type": "error",
                    "payload": {"message": f"未知消息类型: {msg.get('type')}"}
                })

    except (WebSocketDisconnect, asyncio.CancelledError):
        pass
    finally:
        lobby_connections.discard(websocket)


async def send_server_list(ws: WebSocket):
    """向单个客户端发送服务器列表"""
    servers = registry.get_public_list()
    await ws.send_json({"type": "server_list", "payload": {"servers": servers}})


async def broadcast_lobby_update():
    """向所有大厅客户端推送服务器列表更新"""
    if not lobby_connections:
        return
    servers = registry.get_public_list()
    msg = {"type": "server_list_updated", "payload": {"servers": servers}}
    dead = []
    for ws in lobby_connections:
        try:
            await ws.send_json(msg)
        except Exception:
            dead.append(ws)
    for ws in dead:
        lobby_connections.discard(ws)


# ================================================================
# 管理 HTTP 端点
# ================================================================


@app.get("/api/servers")
async def list_servers():
    """获取所有从服务器列表（HTTP）"""
    return {"servers": registry.get_public_list()}


@app.post("/api/servers/{server_id}/enable")
async def enable_server(server_id: str):
    """启用从服务器"""
    info = registry.get_by_id(server_id)
    if not info:
        return JSONResponse(status_code=404, content={"error": "服务器不存在"})

    info.enabled = True
    if info.websocket:
        try:
            await info.websocket.send_json({
                "type": "set_enabled",
                "payload": {"enabled": True},
            })
        except Exception:
            pass
    await broadcast_lobby_update()
    return {"status": "ok", "server_id": server_id, "enabled": True}


@app.post("/api/servers/{server_id}/disable")
async def disable_server(server_id: str):
    """禁用从服务器"""
    info = registry.get_by_id(server_id)
    if not info:
        return JSONResponse(status_code=404, content={"error": "服务器不存在"})

    info.enabled = False
    if info.websocket:
        try:
            await info.websocket.send_json({
                "type": "set_enabled",
                "payload": {"enabled": False},
            })
        except Exception:
            pass
    await broadcast_lobby_update()
    return {"status": "ok", "server_id": server_id, "enabled": False}


@app.delete("/api/servers/{server_id}")
async def remove_server(server_id: str):
    """移除从服务器"""
    if not registry.get_by_id(server_id):
        return JSONResponse(status_code=404, content={"error": "服务器不存在"})
    registry.unregister(server_id)
    await broadcast_lobby_update()
    return {"status": "ok"}


# ================================================================
# 基础端点
# ================================================================


@app.get("/")
async def root():
    return {
        "message": "萝莉丝扑克 - 列表服务器",
        "version": "1.0.0",
        "server_count": len(registry.get_alive()),
    }


@app.get("/health")
async def health():
    return {"status": "ok"}


# ================================================================
# 定时清理任务
# ================================================================


@app.on_event("startup")
async def startup():
    async def cleanup_loop():
        while True:
            await asyncio.sleep(30)
            registry.cleanup_dead()
            await broadcast_lobby_update()

    asyncio.create_task(cleanup_loop())
    logger.info("列表服务器已启动")


if __name__ == "__main__":
    import os
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=int(os.environ.get("MASTER_PORT", "8000")))
