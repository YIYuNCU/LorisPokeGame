import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import time
import pytest
from card_models import Card, Suit, Rank


def pytest_configure(config):
    config.addinivalue_line("markers", "stress: 压力测试（高负载、大量迭代）")
    config.addinivalue_line("markers", "performance: 性能测试（延迟基准、吞吐量）")
    config.addinivalue_line("markers", "memory: 内存管理测试（分配压力、泄漏检测）")
    config.addinivalue_line("markers", "anti_cheat: 反作弊测试（防护验证、漏洞暴露）")
    config.addinivalue_line("markers", "server_load: 服务器负载测试（2H2G 资源约束模拟）")


@pytest.fixture
def full_deck():
    from card_models import create_full_deck
    return create_full_deck()


def c(suit, rank):
    """Shorthand helper to create a Card."""
    return Card(suit=suit, rank=rank)


def create_ws_room(ws, player_name="Alice", is_public=True, turn_timeout=None):
    """Create a room over a TestClient WebSocket and return its room code."""
    payload = {"player_name": player_name, "is_public": is_public}
    if turn_timeout is not None:
        payload["turn_timeout"] = turn_timeout
    ws.send_json({"type": "create_room", "payload": payload})
    resp = ws.receive_json()
    assert resp["type"] == "room_created"
    return resp["payload"]["room_code"]


def join_ws_room(ws, room_code, player_name="Bob"):
    """Join a room over a TestClient WebSocket and return the response."""
    ws.send_json({
        "type": "join_room",
        "payload": {"room_code": room_code, "player_name": player_name},
    })
    resp = ws.receive_json()
    assert resp["type"] == "room_joined"
    return resp


def ready_ws(ws):
    """Mark a TestClient WebSocket player as ready."""
    ws.send_json({"type": "ready", "payload": {}})


def receive_ws_message(ws, msg_type, max_messages=10):
    """Receive messages until the requested type is found."""
    for _ in range(max_messages):
        msg = ws.receive_json()
        if msg["type"] == msg_type:
            return msg
    raise TimeoutError(f"Did not receive {msg_type!r}")


def wait_until(predicate, timeout=10.0, interval=0.2):
    """Poll until predicate returns a truthy value, then return it."""
    deadline = time.monotonic() + timeout
    last_error = None
    while time.monotonic() < deadline:
        try:
            result = predicate()
            if result:
                return result
        except Exception as exc:
            last_error = exc
        time.sleep(interval)
    if last_error is not None:
        raise TimeoutError(f"Condition not met within {timeout}s; last error: {last_error}")
    raise TimeoutError(f"Condition not met within {timeout}s")


def wait_for_server_in_list(fetch_servers, port, timeout=10.0):
    """Poll a master server list until the slave registered on the expected port appears."""
    def find_server():
        servers = fetch_servers()
        return next((server for server in servers if server.get("port") == port), None)

    return wait_until(find_server, timeout=timeout)
