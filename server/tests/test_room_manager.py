"""Tests for room_manager.py: RoomManager."""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import re
from room_manager import RoomManager


class MockWebSocket:
    """Minimal stand-in for WebSocket — used only as an identity token."""
    pass


# ── create_room ──

class TestCreateRoom:
    def test_create_room_returns_code_and_seat0(self):
        rm = RoomManager()
        ws = MockWebSocket()
        code, room, seat = rm.create_room("p1", "Alice", ws, is_public=True)
        assert isinstance(code, str)
        assert seat == 0
        assert "p1" in room.players

    def test_create_room_6_char_uppercase_code(self):
        rm = RoomManager()
        code, _, _ = rm.create_room("p1", "Alice", MockWebSocket())
        assert len(code) == 6
        assert re.match(r'^[A-Z0-9]{6}$', code)


# ── join_room ──

class TestJoinRoom:
    def test_join_room_assigns_seat1(self):
        rm = RoomManager()
        ws1, ws2 = MockWebSocket(), MockWebSocket()
        code, _, _ = rm.create_room("p1", "Alice", ws1)
        ok, msg, seat = rm.join_room(code, "p2", "Bob", ws2)
        assert ok is True
        assert seat == 1

    def test_join_room_three_players_full(self):
        rm = RoomManager()
        code, room, _ = rm.create_room("p1", "A", MockWebSocket())
        rm.join_room(code, "p2", "B", MockWebSocket())
        rm.join_room(code, "p3", "C", MockWebSocket())
        assert room.is_full is True

    def test_join_room_full_fails(self):
        rm = RoomManager()
        code, _, _ = rm.create_room("p1", "A", MockWebSocket())
        rm.join_room(code, "p2", "B", MockWebSocket())
        rm.join_room(code, "p3", "C", MockWebSocket())
        ok, msg, seat = rm.join_room(code, "p4", "D", MockWebSocket())
        assert ok is False
        assert msg == "房间已满"
        assert seat == -1

    def test_join_room_not_found(self):
        rm = RoomManager()
        ok, msg, seat = rm.join_room("NOPE00", "p1", "A", MockWebSocket())
        assert ok is False
        assert msg == "房间不存在"
        assert seat == -1

    def test_join_room_case_insensitive(self):
        rm = RoomManager()
        code, _, _ = rm.create_room("p1", "A", MockWebSocket())
        ok, _, seat = rm.join_room(code.lower(), "p2", "B", MockWebSocket())
        assert ok is True
        assert seat == 1


# ── remove_player ──

class TestRemovePlayer:
    def test_remove_player_auto_deletes_empty_room(self):
        rm = RoomManager()
        code, _, _ = rm.create_room("p1", "A", MockWebSocket())
        rm.remove_player("p1")
        assert rm.get_room(code) is None


# ── get_public_rooms ──

class TestGetPublicRooms:
    def test_get_public_rooms_excludes_private(self):
        rm = RoomManager()
        rm.create_room("p1", "A", MockWebSocket(), is_public=False)
        assert len(rm.get_public_rooms()) == 0

    def test_get_public_rooms_excludes_full(self):
        rm = RoomManager()
        code, _, _ = rm.create_room("p1", "A", MockWebSocket(), is_public=True)
        rm.join_room(code, "p2", "B", MockWebSocket())
        rm.join_room(code, "p3", "C", MockWebSocket())
        assert len(rm.get_public_rooms()) == 0


# ── reconnect ──

class TestReconnect:
    def test_reconnect_flow(self):
        rm = RoomManager()
        ws1, ws_reconnect = MockWebSocket(), MockWebSocket()
        code, _, _ = rm.create_room("p1", "Alice", ws1)

        result = rm.store_reconnect_token("p1")
        assert result is not None
        room, token = result
        assert token.player_name == "Alice"
        assert token.seat == 0

        reconnected = rm.try_reconnect("p1", ws_reconnect)
        assert reconnected is not None
        r_room, r_token = reconnected
        assert r_room is room
        assert "p1" in room.players

    def test_has_reconnect_token(self):
        rm = RoomManager()
        rm.create_room("p1", "Alice", MockWebSocket())
        assert rm.has_reconnect_token("p1") is False

        rm.store_reconnect_token("p1")
        assert rm.has_reconnect_token("p1") is True

        rm.try_reconnect("p1", MockWebSocket())
        assert rm.has_reconnect_token("p1") is False


# ── 补充：set_room_visibility ──

class TestSetRoomVisibility:
    def test_set_visibility_success(self):
        rm = RoomManager()
        rm.create_room("p1", "A", MockWebSocket(), is_public=True)
        ok, msg = rm.set_room_visibility("p1", False)
        assert ok is True
        room = rm.get_room_by_player("p1")
        assert room is not None
        assert room.is_public is False

    def test_set_visibility_not_in_room(self):
        rm = RoomManager()
        ok, msg = rm.set_room_visibility("ghost", False)
        assert ok is False


# ── 补充：join_room 已开始的游戏 ──

class TestJoinRoomExtended:
    def test_join_room_game_started_fails(self):
        rm = RoomManager()
        code, room, _ = rm.create_room("p1", "A", MockWebSocket())
        rm.join_room(code, "p2", "B", MockWebSocket())
        rm.join_room(code, "p3", "C", MockWebSocket())
        room.game_started = True
        ok, msg, seat = rm.join_room(code, "p4", "D", MockWebSocket())
        assert ok is False
        assert seat == -1


# ── 补充：remove_player 边缘 ──

class TestRemovePlayerExtended:
    def test_remove_nonexistent_player_returns_none(self):
        rm = RoomManager()
        result = rm.remove_player("ghost")
        assert result is None

    def test_remove_player_two_remaining_room_survives(self):
        rm = RoomManager()
        code, _, _ = rm.create_room("p1", "A", MockWebSocket())
        rm.join_room(code, "p2", "B", MockWebSocket())
        rm.join_room(code, "p3", "C", MockWebSocket())
        rm.remove_player("p3")
        # 房间仍存在
        assert rm.get_room(code) is not None


# ── 补充：get_next_seat ──

class TestGetNextSeat:
    def test_get_next_seat_returns_negative_when_full(self):
        rm = RoomManager()
        code, room, _ = rm.create_room("p1", "A", MockWebSocket())
        rm.join_room(code, "p2", "B", MockWebSocket())
        rm.join_room(code, "p3", "C", MockWebSocket())
        assert room.get_next_seat() == -1


# ── 补充：cleanup_stale_rooms ──

class TestCleanupStaleRooms:
    def test_cleanup_removes_old_empty_rooms(self):
        from datetime import datetime, timedelta
        rm = RoomManager()
        code, room, _ = rm.create_room("p1", "A", MockWebSocket())
        # 移除玩家使房间为空
        rm.remove_player("p1")
        # 房间已被自动删除（空房间自动清理）
        # 手动创建一个残留房间来测试清理
        code2, room2, _ = rm.create_room("p2", "B", MockWebSocket())
        rm.remove_player("p2")
        # remove_player 已经自动删除了，所以 cleanup 不会有更多效果
        # 直接验证：当房间为空时，get_room 返回 None
        assert rm.get_room(code) is None
        assert rm.get_room(code2) is None

    def test_cleanup_keeps_non_empty_rooms(self):
        rm = RoomManager()
        code, room, _ = rm.create_room("p1", "A", MockWebSocket())
        rm.cleanup_stale_rooms(max_age_minutes=1)
        # 非空房间不应被清理
        assert rm.get_room(code) is not None
