"""
room_manager.py - 房间管理器
处理房间创建、加入、玩家管理和断线处理
"""

import random
import string
import asyncio
import logging
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from typing import Optional
from fastapi import WebSocket

from game_logic import ServerGameManager

logger = logging.getLogger(__name__)


@dataclass
class PlayerConnection:
    """玩家连接信息"""
    player_id: str
    player_name: str
    seat: int
    websocket: WebSocket
    is_ready: bool = False


@dataclass
class ReconnectToken:
    """断线玩家重连令牌"""
    player_id: str
    player_name: str
    seat: int


@dataclass
class Room:
    """游戏房间"""
    room_code: str
    players: dict[str, PlayerConnection] = field(default_factory=dict)  # player_id -> connection
    game: Optional[ServerGameManager] = None
    created_at: datetime = field(default_factory=datetime.now)
    last_activity: datetime = field(default_factory=datetime.now)  # 最后一次人数/状态变化
    game_started: bool = False
    is_public: bool = True
    creator_id: str = ""  # 房间创建者 player_id
    turn_timeout: int = 30  # 出牌超时秒数

    # 断线重连
    reconnect_tokens: dict[str, ReconnectToken] = field(default_factory=dict)  # player_id -> token
    game_paused: bool = False
    vote_state: dict[str, str] = field(default_factory=dict)  # player_id -> "end"/"continue"
    _vote_timeout_task: Optional[asyncio.Task] = field(default=None, repr=False)
    _reconnect_timeout_task: Optional[asyncio.Task] = field(default=None, repr=False)
    _turn_timeout_task: Optional[asyncio.Task] = field(default=None, repr=False)

    @property
    def player_count(self) -> int:
        return len(self.players)

    @property
    def is_full(self) -> bool:
        return self.player_count >= 3

    def get_next_seat(self) -> int:
        """获取下一个可用座位号"""
        used_seats = {p.seat for p in self.players.values()}
        for s in range(3):
            if s not in used_seats:
                return s
        return -1

    def get_player_by_seat(self, seat: int) -> Optional[PlayerConnection]:
        """根据座位号查找玩家"""
        for p in self.players.values():
            if p.seat == seat:
                return p
        return None

    def get_all_websockets(self) -> list[WebSocket]:
        return [p.websocket for p in self.players.values()]


class RoomManager:
    """房间管理器"""

    def __init__(self):
        self._rooms: dict[str, Room] = {}
        self._player_room_map: dict[str, str] = {}  # player_id -> room_code

    @property
    def room_count(self) -> int:
        """当前存在的房间数"""
        return len(self._rooms)

    def create_room(
        self,
        player_id: str,
        player_name: str,
        websocket: WebSocket,
        is_public: bool = True,
        turn_timeout: int = 30,
    ) -> tuple[str, Room, int]:
        """
        创建房间
        返回: (room_code, room, assigned_seat)
        """
        room_code = self._generate_room_code()
        room = Room(
            room_code=room_code,
            is_public=is_public,
            creator_id=player_id,
            turn_timeout=max(10, min(120, turn_timeout)),
        )
        seat = 0

        room.players[player_id] = PlayerConnection(
            player_id=player_id,
            player_name=player_name,
            seat=seat,
            websocket=websocket,
        )

        self._rooms[room_code] = room
        self._player_room_map[player_id] = room_code

        return room_code, room, seat

    def join_room(self, room_code: str, player_id: str, player_name: str, websocket: WebSocket) -> tuple[bool, str, int]:
        """
        加入房间
        返回: (success, message, assigned_seat)
        """
        room_code = room_code.strip().upper()

        if room_code not in self._rooms:
            return False, "房间不存在", -1

        room = self._rooms[room_code]

        if room.is_full:
            return False, "房间已满", -1

        if room.game_started:
            return False, "游戏已开始", -1

        seat = room.get_next_seat()
        if seat < 0:
            return False, "无法分配座位", -1

        room.players[player_id] = PlayerConnection(
            player_id=player_id,
            player_name=player_name,
            seat=seat,
            websocket=websocket,
        )

        self._player_room_map[player_id] = room_code
        room.last_activity = datetime.now()

        return True, "", seat

    def get_room_by_player(self, player_id: str) -> Optional[Room]:
        """根据玩家ID查找房间"""
        room_code = self._player_room_map.get(player_id)
        if room_code:
            return self._rooms.get(room_code)
        return None

    def remove_player(self, player_id: str) -> Optional[Room]:
        """移除玩家，重排座位号，返回所在房间（如果房间还存在）"""
        room_code = self._player_room_map.pop(player_id, None)
        if not room_code or room_code not in self._rooms:
            return None

        room = self._rooms[room_code]
        old_seat = room.players[player_id].seat if player_id in room.players else -1
        room.players.pop(player_id, None)
        room.last_activity = datetime.now()

        # 如果房间空了，删除房间
        if room.player_count == 0:
            del self._rooms[room_code]
            return None

        # 游戏未开始时重排座位号，保持连续（0, 1, 2...）
        if not room.game_started:
            sorted_players = sorted(room.players.values(), key=lambda p: p.seat)
            for new_seat, conn in enumerate(sorted_players):
                conn.seat = new_seat

        return room

    def get_room(self, room_code: str) -> Optional[Room]:
        return self._rooms.get(room_code.strip().upper())

    def get_public_rooms(self) -> list[dict]:
        """获取所有公开且未开始的房间列表"""
        result = []
        for code, room in self._rooms.items():
            if room.is_public and not room.game_started and not room.is_full:
                result.append({
                    "room_code": code,
                    "player_count": room.player_count,
                    "max_players": 3,
                    "players": [p.player_name for p in room.players.values()],
                })
        return result

    def set_room_visibility(self, player_id: str, is_public: bool) -> tuple[bool, str]:
        """设置房间可见性，仅创建者可操作，游戏中禁止。返回 (success, message)"""
        room_code = self._player_room_map.get(player_id)
        if not room_code or room_code not in self._rooms:
            return False, "你不在任何房间中"
        room = self._rooms[room_code]
        if room.creator_id != player_id:
            return False, "仅房间创建者可修改可见性"
        if room.game_started:
            return False, "游戏进行中不可修改可见性"
        room.is_public = is_public
        return True, ""

    def store_reconnect_token(self, player_id: str) -> Optional[tuple[Room, ReconnectToken]]:
        """存储断线玩家的重连令牌，返回 (room, token) 或 None"""
        room_code = self._player_room_map.pop(player_id, None)
        if not room_code or room_code not in self._rooms:
            return None

        room = self._rooms[room_code]
        conn = room.players.pop(player_id, None)
        if not conn:
            return None

        token = ReconnectToken(
            player_id=player_id,
            player_name=conn.player_name,
            seat=conn.seat,
        )
        room.reconnect_tokens[player_id] = token
        return room, token

    def try_reconnect(self, player_id: str, websocket: WebSocket) -> Optional[tuple[Room, ReconnectToken]]:
        """尝试重连，成功返回 (room, token) 并恢复玩家连接"""
        for room in self._rooms.values():
            if player_id in room.reconnect_tokens:
                token = room.reconnect_tokens[player_id]
                # 恢复玩家连接
                room.players[player_id] = PlayerConnection(
                    player_id=player_id,
                    player_name=token.player_name,
                    seat=token.seat,
                    websocket=websocket,
                )
                self._player_room_map[player_id] = room.room_code
                del room.reconnect_tokens[player_id]
                # 清除暂停和投票状态
                room.game_paused = False
                room.vote_state.clear()
                return room, token
        return None

    def has_reconnect_token(self, player_id: str) -> bool:
        """检查是否有该玩家的重连令牌"""
        for room in self._rooms.values():
            if player_id in room.reconnect_tokens:
                return True
        return False

    def cleanup_stale_rooms(self, max_age_minutes: int = 30):
        """清理超时房间：
        1. 空房间超过 max_age_minutes
        2. 仅剩1人且超过5分钟未变化
        """
        now = datetime.now()
        empty_cutoff = now - timedelta(minutes=max_age_minutes)
        single_cutoff = now - timedelta(minutes=5)
        to_remove = []
        for code, room in self._rooms.items():
            if room.player_count == 0 and room.created_at < empty_cutoff:
                to_remove.append(code)
            elif room.player_count == 1 and not room.game_started and room.last_activity < single_cutoff:
                to_remove.append(code)
        for code in to_remove:
            logger.info(f"清理超时房间: {code}")
            # 清理 player_room_map
            pids = [pid for pid, rc in self._player_room_map.items() if rc == code]
            for pid in pids:
                del self._player_room_map[pid]
            del self._rooms[code]

    def _generate_room_code(self) -> str:
        """生成6位大写字母数字房间码"""
        while True:
            code = ''.join(random.choices(string.ascii_uppercase + string.digits, k=6))
            if code not in self._rooms:
                return code
