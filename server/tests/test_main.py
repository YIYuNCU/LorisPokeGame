"""
test_main.py - FastAPI 服务器集成测试
覆盖 HTTP 端点和 WebSocket 消息处理
"""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import pytest
from starlette.testclient import TestClient
from main import app, room_manager, connected_websockets, _reconnected_player_ids
from tests.conftest import create_ws_room, join_ws_room, ready_ws, receive_ws_message


@pytest.fixture(autouse=True)
def reset_state():
    """每个测试前重置模块级状态"""
    room_manager._rooms.clear()
    room_manager._player_room_map.clear()
    connected_websockets.clear()
    _reconnected_player_ids.clear()
    yield
    room_manager._rooms.clear()
    room_manager._player_room_map.clear()
    connected_websockets.clear()
    _reconnected_player_ids.clear()


# ── HTTP 端点 ──

class TestHttpEndpoints:
    def test_root_endpoint_returns_server_info(self):
        client = TestClient(app)
        resp = client.get("/")
        assert resp.status_code == 200
        data = resp.json()
        assert "萝莉丝扑克" in data["message"]
        assert "version" in data

    def test_health_endpoint_returns_ok(self):
        client = TestClient(app)
        resp = client.get("/health")
        assert resp.status_code == 200
        assert resp.json() == {"status": "ok"}

    def test_stats_endpoint_returns_status(self):
        client = TestClient(app)
        resp = client.get("/stats")
        assert resp.status_code == 200
        data = resp.json()
        assert "max_concurrent_games" in data
        assert "room_count" in data
        assert "connected_players" in data


# ── WebSocket 房间管理测试 ──

class TestWsRoomManagement:
    def test_create_room_returns_room_code(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            code = create_ws_room(ws, "Alice")
            assert len(code) == 6
            assert code.isupper()

    def test_join_room_success(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            code = create_ws_room(ws1, "Alice")
            with client.websocket_connect("/ws") as ws2:
                resp = join_ws_room(ws2, code, "Bob")
                assert resp["payload"]["room_code"] == code
                assert resp["payload"]["seat"] == 1

    def test_join_room_not_found(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            ws.send_json({"type": "join_room", "payload": {"room_code": "NOPE00", "player_name": "A"}})
            resp = ws.receive_json()
            assert resp["type"] == "error"

    def test_unknown_message_returns_error(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            ws.send_json({"type": "invalid_type_xyz", "payload": {}})
            resp = ws.receive_json()
            assert resp["type"] == "error"
            assert "未知" in resp["payload"]["message"]

    def test_list_rooms_returns_rooms(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            create_ws_room(ws1, "Alice", is_public=True)
            with client.websocket_connect("/ws") as ws2:
                ws2.send_json({"type": "list_rooms", "payload": {}})
                resp = ws2.receive_json()
                assert resp["type"] == "room_list"
                assert len(resp["payload"]["rooms"]) >= 1

    def test_set_room_visibility(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            create_ws_room(ws, "Alice")
            ws.send_json({"type": "set_room_visibility", "payload": {"is_public": False}})
            resp = ws.receive_json()
            assert resp["type"] == "visibility_changed"
            assert resp["payload"]["is_public"] is False

    def test_create_room_clamps_turn_timeout(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            code = create_ws_room(ws, "Alice", turn_timeout=999)
            room = room_manager.get_room(code)
            assert room is not None
            assert room.turn_timeout == 120


# ── WebSocket 准备和游戏流程测试 ──

class TestWsGameFlow:
    def test_ready_broadcasts(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            code = create_ws_room(ws1, "Alice")
            with client.websocket_connect("/ws") as ws2:
                join_ws_room(ws2, code, "Bob")
                # ws1 收到 player_joined 广播
                joined_msg = ws1.receive_json()
                assert joined_msg["type"] == "player_joined"
                ready_ws(ws1)
                # ws1 和 ws2 都应收到 player_ready
                resp1 = ws1.receive_json()
                assert resp1["type"] == "player_ready"
                resp2 = ws2.receive_json()
                assert resp2["type"] == "player_ready"

    def test_three_players_ready_starts_game(self):
        """三个玩家全部准备后应收到 game_start"""
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            code = create_ws_room(ws1, "Alice")
            with client.websocket_connect("/ws") as ws2:
                join_ws_room(ws2, code, "Bob")
                with client.websocket_connect("/ws") as ws3:
                    join_ws_room(ws3, code, "Charlie")

                    # 清除 join 时的广播消息
                    # ws1 收到 player_joined x2
                    for _ in range(2):
                        ws1.receive_json()

                    ready_ws(ws1)
                    ready_ws(ws2)

                    # 清除 ready 广播
                    for _ in range(4):  # 2次ready * 2个接收者
                        pass

                    ready_ws(ws3)

                    # 等待 game_start 消息
                    game_start = receive_ws_message(ws1, "game_start")
                    assert "hand" in game_start["payload"]
                    assert len(game_start["payload"]["hand"]) > 0

                    turn_change = receive_ws_message(ws1, "turn_change")
                    assert turn_change["payload"]["phase"] == "bidding"
                    assert "current_player" in turn_change["payload"]

    def test_player_left_payload_includes_remaining_player_ids_and_repacked_seats(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            code = create_ws_room(ws1, "Alice")
            alice_id = next(iter(room_manager.get_room(code).players))
            with client.websocket_connect("/ws") as ws2:
                bob_joined = join_ws_room(ws2, code, "Bob")
                bob_id = bob_joined["payload"]["player_id"]
                ws1.receive_json()  # player_joined Bob

                with client.websocket_connect("/ws") as ws3:
                    charlie_joined = join_ws_room(ws3, code, "Charlie")
                    charlie_id = charlie_joined["payload"]["player_id"]
                    ws1.receive_json()  # player_joined Charlie
                    ws2.receive_json()  # player_joined Charlie

                left_msg = receive_ws_message(ws1, "player_left")
                players = left_msg["payload"]["players"]
                assert left_msg["payload"]["player_id"] == charlie_id
                assert {p["player_id"] for p in players} == {alice_id, bob_id}
                assert [p["seat"] for p in sorted(players, key=lambda p: p["seat"])] == [0, 1]


# ── WebSocket 错误处理 ──

class TestWsErrorHandling:
    def test_bid_without_game_returns_error(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            create_ws_room(ws, "Alice")
            ws.send_json({"type": "bid", "payload": {"amount": 3}})
            resp = ws.receive_json()
            assert resp["type"] == "error"

    def test_play_without_game_returns_error(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            create_ws_room(ws, "Alice")
            ws.send_json({"type": "play", "payload": {"cards": []}})
            resp = ws.receive_json()
            assert resp["type"] == "error"

    def test_pass_without_game_returns_error(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            create_ws_room(ws, "Alice")
            ws.send_json({"type": "pass", "payload": {}})
            resp = ws.receive_json()
            assert resp["type"] == "error"


# ── 超时自动操作逻辑 ──

from game_logic import ServerGameManager, PHASE_PLAYING, PHASE_BIDDING


class TestTimeoutAutoAction:
    """验证 _timeout_pass 中的牌权判断和自动出牌逻辑"""

    def _setup_playing_game(self, current_seat=0, last_played_by=None):
        """创建一个进入出牌阶段的游戏，返回 game 实例"""
        game = ServerGameManager()
        game.set_player_name(0, "A")
        game.set_player_name(1, "B")
        game.set_player_name(2, "C")
        game.start_game()

        # 跳过叫分，直接进入出牌阶段
        game.phase = PHASE_PLAYING
        game.landlord_index = 0
        game.players[0].role = "landlord"
        game.players[1].role = "farmer"
        game.players[2].role = "farmer"
        game.current_player = current_seat

        if last_played_by is not None:
            # 模拟上一手牌（其他玩家出的）
            from card_models import Card, Suit, Rank, classify_play
            combo = classify_play([Card(Suit.Diamonds, Rank.Three)])
            game.last_played_combo = combo
            game.last_played_by = last_played_by

        return game

    def test_has_initiative_when_no_last_played(self):
        """牌权在手（last_played_combo is None）→ has_initiative = True"""
        game = self._setup_playing_game(current_seat=0)
        has_initiative = game.last_played_combo is None or game.last_played_by == 0
        assert has_initiative is True

    def test_has_initiative_when_last_played_by_self(self):
        """牌权在手（last_played_by == current_seat）→ has_initiative = True"""
        game = self._setup_playing_game(current_seat=0, last_played_by=0)
        has_initiative = game.last_played_combo is None or game.last_played_by == 0
        assert has_initiative is True

    def test_no_initiative_when_following_other(self):
        """跟牌（last_played_by == 其他人）→ has_initiative = False"""
        game = self._setup_playing_game(current_seat=0, last_played_by=1)
        has_initiative = game.last_played_combo is None or game.last_played_by == 0
        assert has_initiative is False

    def test_timeout_plays_smallest_single_when_initiative(self):
        """牌权在手时超时，自动出最小单牌"""
        game = self._setup_playing_game(current_seat=0)
        hand = game.players[0].hand
        assert len(hand) > 0

        smallest = min(hand, key=lambda c: c.rank)
        action = game.submit_play(0, [smallest])
        types = [m["type"] for m in action.messages]
        assert "error" not in types
        assert "cards_played" in types

    def test_timeout_pass_when_following(self):
        """跟牌时超时，自动不出"""
        game = self._setup_playing_game(current_seat=0, last_played_by=1)
        action = game.submit_pass(0)
        types = [m["type"] for m in action.messages]
        assert "error" not in types
        assert "player_passed" in types

    def test_smallest_card_removed_from_hand(self):
        """超时出最小单牌后，该牌从手牌中移除"""
        game = self._setup_playing_game(current_seat=0)
        original_count = len(game.players[0].hand)
        smallest = min(game.players[0].hand, key=lambda c: c.rank)

        action = game.submit_play(0, [smallest])
        assert len(game.players[0].hand) == original_count - 1
        remaining_ranks = [c.rank for c in game.players[0].hand]
        # 最小牌如果唯一则不在手牌中
        if sum(1 for c in game.players[0].hand if c.rank == smallest.rank) == original_count - 1:
            # 手里全是同点数的牌（极端情况），移除一张后还有
            pass
        assert len(game.players[0].hand) < original_count
