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
        assert "active_games" in data


# ── WebSocket 辅助函数 ──

def _create_room(ws, player_name="Alice", is_public=True):
    """创建房间并返回 room_code"""
    ws.send_json({"type": "create_room", "payload": {"player_name": player_name, "is_public": is_public}})
    resp = ws.receive_json()
    assert resp["type"] == "room_created"
    return resp["payload"]["room_code"]


def _join_room(ws, room_code, player_name="Bob"):
    """加入房间"""
    ws.send_json({"type": "join_room", "payload": {"room_code": room_code, "player_name": player_name}})
    resp = ws.receive_json()
    assert resp["type"] == "room_joined"
    return resp


def _ready(ws):
    """发送准备"""
    ws.send_json({"type": "ready", "payload": {}})


def _receive_until(ws, msg_type, max_msgs=10):
    """接收消息直到找到指定类型"""
    for _ in range(max_msgs):
        msg = ws.receive_json()
        if msg["type"] == msg_type:
            return msg
    raise TimeoutError(f"未收到 {msg_type} 消息")


# ── WebSocket 房间管理测试 ──

class TestWsRoomManagement:
    def test_create_room_returns_room_code(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            code = _create_room(ws, "Alice")
            assert len(code) == 6
            assert code.isupper()

    def test_join_room_success(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            code = _create_room(ws1, "Alice")
            with client.websocket_connect("/ws") as ws2:
                resp = _join_room(ws2, code, "Bob")
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
            _create_room(ws1, "Alice", is_public=True)
            with client.websocket_connect("/ws") as ws2:
                ws2.send_json({"type": "list_rooms", "payload": {}})
                resp = ws2.receive_json()
                assert resp["type"] == "room_list"
                assert len(resp["payload"]["rooms"]) >= 1

    def test_set_room_visibility(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            _create_room(ws, "Alice")
            ws.send_json({"type": "set_room_visibility", "payload": {"is_public": False}})
            resp = ws.receive_json()
            assert resp["type"] == "visibility_changed"
            assert resp["payload"]["is_public"] is False


# ── WebSocket 准备和游戏流程测试 ──

class TestWsGameFlow:
    def test_ready_broadcasts(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            code = _create_room(ws1, "Alice")
            with client.websocket_connect("/ws") as ws2:
                _join_room(ws2, code, "Bob")
                # ws1 收到 player_joined 广播
                joined_msg = ws1.receive_json()
                assert joined_msg["type"] == "player_joined"
                _ready(ws1)
                # ws1 和 ws2 都应收到 player_ready
                resp1 = ws1.receive_json()
                assert resp1["type"] == "player_ready"
                resp2 = ws2.receive_json()
                assert resp2["type"] == "player_ready"

    def test_three_players_ready_starts_game(self):
        """三个玩家全部准备后应收到 game_start"""
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws1:
            code = _create_room(ws1, "Alice")
            with client.websocket_connect("/ws") as ws2:
                _join_room(ws2, code, "Bob")
                with client.websocket_connect("/ws") as ws3:
                    _join_room(ws3, code, "Charlie")

                    # 清除 join 时的广播消息
                    # ws1 收到 player_joined x2
                    for _ in range(2):
                        ws1.receive_json()

                    _ready(ws1)
                    _ready(ws2)

                    # 清除 ready 广播
                    for _ in range(4):  # 2次ready * 2个接收者
                        pass

                    _ready(ws3)

                    # 等待 game_start 消息
                    game_start = _receive_until(ws1, "game_start")
                    assert "hand" in game_start["payload"]
                    assert len(game_start["payload"]["hand"]) > 0


# ── WebSocket 错误处理 ──

class TestWsErrorHandling:
    def test_bid_without_game_returns_error(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            _create_room(ws, "Alice")
            ws.send_json({"type": "bid", "payload": {"amount": 3}})
            resp = ws.receive_json()
            assert resp["type"] == "error"

    def test_play_without_game_returns_error(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            _create_room(ws, "Alice")
            ws.send_json({"type": "play", "payload": {"cards": []}})
            resp = ws.receive_json()
            assert resp["type"] == "error"

    def test_pass_without_game_returns_error(self):
        client = TestClient(app)
        with client.websocket_connect("/ws") as ws:
            _create_room(ws, "Alice")
            ws.send_json({"type": "pass", "payload": {}})
            resp = ws.receive_json()
            assert resp["type"] == "error"
