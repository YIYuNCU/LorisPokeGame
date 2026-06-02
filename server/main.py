"""
main.py - FastAPI 斗地主游戏服务器
WebSocket 实时通信，房间管理和游戏逻辑
"""

import asyncio
import json
import logging
from uuid import uuid4

from fastapi import FastAPI, WebSocket, WebSocketDisconnect, Query
from fastapi.responses import JSONResponse

from models import ClientMessage, CreateRoomPayload, JoinRoomPayload, BidPayload, PlayPayload, CardData, SetRoomVisibilityPayload
from room_manager import RoomManager, Room, ReconnectToken
from game_logic import ServerGameManager
from card_models import Card
from server_config import ServerConfig

# 配置日志
logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger(__name__)

app = FastAPI(title="萝莉丝扑克 - 斗地主服务器", version="1.0.0")
room_manager = RoomManager()
server_config = ServerConfig()
connected_websockets: set[WebSocket] = set()  # 所有已连接的 WebSocket
_reconnected_player_ids: set[str] = set()  # 刚完成重连的玩家（防止旧连接 disconnect 误触）


async def send_json(websocket: WebSocket, data: dict):
    """发送 JSON 消息"""
    try:
        await websocket.send_json(data)
    except Exception as e:
        logger.error(f"发送消息失败: {e}")


async def broadcast_to_room(room: Room, message: dict, exclude_player: str = None):
    """向房间内所有玩家广播消息（可排除指定玩家）"""
    tasks = []
    for pid, conn in room.players.items():
        if pid != exclude_player:
            tasks.append(send_json(conn.websocket, message))
    if tasks:
        await asyncio.gather(*tasks, return_exceptions=True)


def cancel_turn_timer(room: Room):
    """取消当前出牌超时计时器"""
    if room._turn_timeout_task and not room._turn_timeout_task.done():
        room._turn_timeout_task.cancel()
        room._turn_timeout_task = None


async def start_turn_timer(room: Room):
    """为当前出牌玩家启动超时计时器，超时自动不出"""
    cancel_turn_timer(room)
    if room.turn_timeout <= 0 or not room.game or room.game_paused:
        return

    timeout = room.turn_timeout

    async def _timeout_pass():
        try:
            await asyncio.sleep(timeout)
        except asyncio.CancelledError:
            return
        # 超时：自动不出
        if not room.game or room.game_paused:
            return
        game_data = room.game.get_game_data()
        current_seat = game_data.get("current_player", -1)
        phase = game_data.get("phase", "")
        if phase != "playing":
            return
        logger.info(f"房间 {room.room_code} 座位 {current_seat} 出牌超时({timeout}s)，自动不出")
        action = room.game.submit_pass(current_seat)
        for msg in action.messages:
            msg_type = msg.get("type", "")
            payload = msg.get("payload", {})
            if msg.get("broadcast"):
                await broadcast_to_room(room, {"type": msg_type, "payload": payload})
            if msg_type == "turn_change":
                start_turn_timer_soon(room)

    room._turn_timeout_task = asyncio.ensure_future(_timeout_pass())


def start_turn_timer_soon(room: Room):
    """安全地调度 start_turn_timer（避免嵌套 await 问题）"""
    asyncio.ensure_future(start_turn_timer(room))


async def broadcast_lobby_update():
    """向所有不在房间内的客户端广播房间列表更新"""
    public_rooms = room_manager.get_public_rooms()
    message = {
        "type": "room_list_updated",
        "payload": {"rooms": public_rooms}
    }
    tasks = []
    for ws in connected_websockets:
        # 跳过已在房间中的 WebSocket
        in_room = any(
            any(p.websocket is ws for p in room.players.values())
            for room in room_manager._rooms.values()
        )
        if not in_room:
            tasks.append(send_json(ws, message))
    if tasks:
        await asyncio.gather(*tasks, return_exceptions=True)


async def send_to_seat(room: Room, seat: int, message: dict):
    """向指定座位的玩家发送消息"""
    player = room.get_player_by_seat(seat)
    if player:
        await send_json(player.websocket, message)


async def handle_create_room(websocket: WebSocket, player_id: str, payload: dict):
    """处理创建房间请求"""
    try:
        data = CreateRoomPayload(**payload)
    except Exception:
        await send_json(websocket, {"type": "error", "payload": {"message": "无效的创建房间参数"}})
        return

    room_code, room, seat = room_manager.create_room(player_id, data.player_name, websocket, is_public=data.is_public)
    logger.info(f"房间 {room_code} 已创建，玩家 {data.player_name} (ID:{player_id}, 座位:{seat}, 公开:{data.is_public})")

    await send_json(websocket, {
        "type": "room_created",
        "payload": {
            "room_code": room_code,
            "player_id": player_id,
            "seat": seat,
        }
    })

    # 如果房间公开，广播大厅更新
    if data.is_public:
        await broadcast_lobby_update()


async def handle_join_room(websocket: WebSocket, player_id: str, payload: dict):
    """处理加入房间请求"""
    try:
        data = JoinRoomPayload(**payload)
    except Exception:
        await send_json(websocket, {"type": "error", "payload": {"message": "无效的加入房间参数"}})
        return

    success, msg, seat = room_manager.join_room(data.room_code, player_id, data.player_name, websocket)

    if not success:
        await send_json(websocket, {"type": "error", "payload": {"message": msg}})
        return

    room = room_manager.get_room_by_player(player_id)
    logger.info(f"玩家 {data.player_name} (ID:{player_id}) 加入房间 {data.room_code}，座位:{seat}")

    # 构建当前房间内所有玩家信息
    players_info = [
        {"seat": p.seat, "name": p.player_name, "ready": p.is_ready}
        for p in room.players.values()
    ]

    # 通知加入者成功（含房间内已有玩家列表）
    await send_json(websocket, {
        "type": "room_joined",
        "payload": {
            "room_code": data.room_code.strip().upper(),
            "player_id": player_id,
            "seat": seat,
            "players": players_info,
        }
    })

    # 通知房间内其他人（含更新后的完整玩家列表）
    await broadcast_to_room(room, {
        "type": "player_joined",
        "payload": {
            "player_id": player_id,
            "player_name": data.player_name,
            "seat": seat,
            "players": players_info,
        }
    }, exclude_player=player_id)

    # 人数变化，广播大厅更新
    await broadcast_lobby_update()


async def handle_ready(websocket: WebSocket, player_id: str, payload: dict):
    """处理准备就绪请求"""
    room = room_manager.get_room_by_player(player_id)
    if not room:
        await send_json(websocket, {"type": "error", "payload": {"message": "你不在任何房间中"}})
        return

    player = room.players.get(player_id)
    if not player:
        return

    # 如果上一局已结束，重置游戏状态
    if room.game_started and room.game and room.game.phase == 4:  # PHASE_GAMEOVER
        room.game_started = False

    if room.game_started:
        await send_json(websocket, {"type": "error", "payload": {"message": "游戏进行中，请等待"}})
        return

    player.is_ready = True
    ready_count = sum(1 for p in room.players.values() if p.is_ready)

    # 通知所有人当前准备进度（含每位玩家状态）
    players_info = [
        {"seat": p.seat, "name": p.player_name, "ready": p.is_ready}
        for p in room.players.values()
    ]
    await broadcast_to_room(room, {
        "type": "player_ready",
        "payload": {
            "player_id": player_id,
            "seat": player.seat,
            "ready_count": ready_count,
            "total_needed": 3,
            "players": players_info,
        }
    })

    # 检查是否所有人都准备好了
    if room.player_count == 3 and all(p.is_ready for p in room.players.values()):
        await start_game_in_room(room)


async def handle_cancel_ready(websocket: WebSocket, player_id: str, payload: dict):
    """处理取消准备请求"""
    room = room_manager.get_room_by_player(player_id)
    if not room:
        return

    player = room.players.get(player_id)
    if not player:
        return

    player.is_ready = False
    ready_count = sum(1 for p in room.players.values() if p.is_ready)

    # 通知所有人准备进度变化（含每位玩家状态）
    players_info = [
        {"seat": p.seat, "name": p.player_name, "ready": p.is_ready}
        for p in room.players.values()
    ]
    await broadcast_to_room(room, {
        "type": "player_ready",
        "payload": {
            "player_id": player_id,
            "seat": player.seat,
            "ready_count": ready_count,
            "total_needed": 3,
            "players": players_info,
        }
    })


async def handle_list_rooms(websocket: WebSocket):
    """处理房间列表请求"""
    rooms = room_manager.get_public_rooms()
    await send_json(websocket, {
        "type": "room_list",
        "payload": {"rooms": rooms}
    })


async def handle_set_room_visibility(websocket: WebSocket, player_id: str, payload: dict):
    """处理设置房间可见性请求"""
    try:
        data = SetRoomVisibilityPayload(**payload)
    except Exception:
        await send_json(websocket, {"type": "error", "payload": {"message": "无效的参数"}})
        return

    success, msg = room_manager.set_room_visibility(player_id, data.is_public)
    if not success:
        await send_json(websocket, {"type": "error", "payload": {"message": msg}})
        return

    # 确认给请求者
    await send_json(websocket, {
        "type": "visibility_changed",
        "payload": {"is_public": data.is_public}
    })

    # 广播大厅更新
    await broadcast_lobby_update()


async def start_game_in_room(room: Room):
    """在房间内开始游戏"""
    # 检查并发对局上限
    if not server_config.can_start_game():
        await broadcast_to_room(room, {
            "type": "error",
            "payload": {"message": f"服务器对局已满（{server_config.active_games}/{server_config.max_concurrent_games}），请稍后再试"}
        })
        # 重置准备状态
        for p in room.players.values():
            p.is_ready = False
        await broadcast_to_room(room, {
            "type": "player_ready",
            "payload": {
                "ready_count": 0,
                "total_needed": 3,
                "players": [
                    {"seat": p.seat, "name": p.player_name, "ready": p.is_ready}
                    for p in room.players.values()
                ],
            }
        })
        return

    room.game_started = True
    room.game = ServerGameManager()
    room.dealing_ready.clear()  # 重置发牌确认集合

    server_config.on_game_start()

    # 重置所有玩家的准备状态（为下一局做准备）
    for p in room.players.values():
        p.is_ready = False

    # 设置玩家名称
    for pid, conn in room.players.items():
        room.game.set_player_name(conn.seat, conn.player_name)

    # 发牌
    game_data = room.game.start_game()
    logger.info(f"房间 {room.room_code} 游戏开始！")

    # 向每个玩家发送各自的手牌
    # 按座位 0,1,2 顺序拼接玩家名
    names_by_seat = ["玩家", "玩家", "玩家"]
    for pid, conn in room.players.items():
        names_by_seat[conn.seat] = conn.player_name

    for pid, conn in room.players.items():
        await send_json(conn.websocket, {
            "type": "game_start",
            "payload": {
                "hand": game_data["hands"][conn.seat],
                "kitty_count": 3,
                "first_bidder": game_data["first_bidder"],
                "player_names": names_by_seat,
            }
        })

    # 暂存叫分阶段指令，等所有客户端完成发牌动画后再发送
    room.pending_turn_change = {
        "type": "turn_change",
        "payload": {
            "current_player": game_data["first_bidder"],
            "phase": "bidding"
        }
    }

    # 游戏开始，广播大厅更新（该房间已不在大厅中）
    await broadcast_lobby_update()


async def handle_dealing_complete(player_id: str):
    """处理客户端发牌动画完成确认"""
    room = room_manager.get_room_by_player(player_id)
    if not room or not room.game:
        return

    room.dealing_ready.add(player_id)
    logger.info(f"房间 {room.room_code} 玩家 {player_id} 发牌完成 ({len(room.dealing_ready)}/{room.player_count})")

    # 所有玩家都完成了发牌动画，下发叫分阶段指令
    if len(room.dealing_ready) >= room.player_count and room.pending_turn_change:
        msg = room.pending_turn_change
        room.pending_turn_change = None
        # 注入出牌超时
        if msg.get("type") == "turn_change" and msg.get("payload", {}).get("phase") == "playing":
            msg["payload"]["turn_timeout"] = room.turn_timeout
        await broadcast_to_room(room, msg)
        # 出牌阶段启动超时计时器
        if msg.get("type") == "turn_change" and msg.get("payload", {}).get("phase") == "playing":
            start_turn_timer_soon(room)
        logger.info(f"房间 {room.room_code} 所有玩家就绪，下发叫分指令")


async def handle_bid(websocket: WebSocket, player_id: str, payload: dict):
    """处理叫分"""
    room = room_manager.get_room_by_player(player_id)
    if not room or not room.game:
        await send_json(websocket, {"type": "error", "payload": {"message": "游戏未开始"}})
        return

    player = room.players.get(player_id)
    if not player:
        return

    try:
        data = BidPayload(**payload)
    except Exception:
        await send_json(websocket, {"type": "error", "payload": {"message": "无效的叫分参数"}})
        return

    result = room.game.submit_bid(player.seat, data.amount)
    await _dispatch_game_messages(room, result.messages)


async def handle_play(websocket: WebSocket, player_id: str, payload: dict):
    """处理出牌"""
    room = room_manager.get_room_by_player(player_id)
    if not room or not room.game:
        await send_json(websocket, {"type": "error", "payload": {"message": "游戏未开始"}})
        return

    player = room.players.get(player_id)
    if not player:
        return

    try:
        data = PlayPayload(**payload)
    except Exception:
        await send_json(websocket, {"type": "error", "payload": {"message": "无效的出牌参数"}})
        return

    cards = [Card(suit=c.suit, rank=c.rank) for c in data.cards]
    # 取消当前出牌超时（玩家已行动）
    cancel_turn_timer(room)

    result = room.game.submit_play(player.seat, cards)
    await _dispatch_game_messages(room, result.messages)


async def handle_pass(websocket: WebSocket, player_id: str, payload: dict):
    """处理不出"""
    room = room_manager.get_room_by_player(player_id)
    if not room or not room.game:
        await send_json(websocket, {"type": "error", "payload": {"message": "游戏未开始"}})
        return

    player = room.players.get(player_id)
    if not player:
        return

    # 取消当前出牌超时（玩家已行动）
    cancel_turn_timer(room)

    result = room.game.submit_pass(player.seat)
    await _dispatch_game_messages(room, result.messages)


async def _dispatch_game_messages(room: Room, messages: list[dict]):
    """分发游戏消息：广播消息发给所有人，定向消息发给指定座位
    如果消息中包含 game_start，拦截后续的 turn_change，等客户端发牌动画完成后再发送
    """
    # 检测是否包含 game_start（重发牌场景）
    has_game_start = any(m.get("type") == "game_start" for m in messages)
    if has_game_start:
        room.dealing_ready.clear()

    # 检测是否包含 game_over
    has_game_over = any(m.get("type") == "game_over" for m in messages)

    for msg in messages:
        msg_type = msg.get("type", "")
        payload = msg.get("payload", {})

        # 如果有 game_start 且当前消息是 turn_change，暂存不发
        if has_game_start and msg_type == "turn_change" and msg.get("broadcast"):
            room.pending_turn_change = {"type": msg_type, "payload": payload}
            logger.info(f"房间 {room.room_code} 暂存 turn_change，等待客户端发牌完成")
            continue

        if msg.get("broadcast"):
            # 注入出牌超时到 turn_change
            if msg_type == "turn_change" and payload.get("phase") == "playing":
                payload["turn_timeout"] = room.turn_timeout
            # 广播给所有人
            await broadcast_to_room(room, {"type": msg_type, "payload": payload})
            # 出牌阶段 turn_change 时启动超时计时器
            if msg_type == "turn_change" and payload.get("phase") == "playing":
                start_turn_timer_soon(room)
        elif "target" in msg:
            # 定向发送
            target_seat = msg["target"]
            await send_to_seat(room, target_seat, {"type": msg_type, "payload": payload})

    # 对局正常结束，释放并发槽位
    if has_game_over:
        server_config.on_game_end()


async def handle_disconnect(player_id: str, disconnecting_ws: WebSocket = None):
    """处理玩家断开连接
    disconnecting_ws: 正在断开的 WebSocket，用于区分旧连接清理和真正断线
    """
    # 如果该玩家刚完成重连，这是旧连接的关闭事件，跳过
    if player_id in _reconnected_player_ids:
        _reconnected_player_ids.discard(player_id)
        logger.info(f"玩家 {player_id} 已重连，忽略旧连接断开事件")
        return

    room = room_manager.get_room_by_player(player_id)

    # 检查是否是旧连接的断开（玩家已通过新连接重连）
    if room and disconnecting_ws:
        current_conn = room.players.get(player_id)
        if current_conn and current_conn.websocket is not disconnecting_ws:
            logger.info(f"玩家 {player_id} 旧连接关闭，当前已是新连接，跳过")
            return

    # 游戏进行中断线 → 存储重连令牌 + 发起投票
    if room and room.game_started and room.game and room.game.phase != 4:
        result = room_manager.store_reconnect_token(player_id)
        if result:
            room, token = result
            logger.info(f"玩家 {token.player_name} (座位:{token.seat}) 游戏中断线，启动重连投票")

            # 暂停游戏
            room.game_paused = True
            room.vote_state.clear()

            # 通知剩余玩家
            players_info = [
                {"seat": p.seat, "name": p.player_name, "ready": p.is_ready}
                for p in room.players.values()
            ]
            await broadcast_to_room(room, {
                "type": "player_left",
                "payload": {
                    "player_id": player_id,
                    "players": players_info,
                }
            })

            # 发起投票
            await broadcast_to_room(room, {
                "type": "vote_start",
                "payload": {
                    "disconnected_player": token.player_name,
                    "disconnected_seat": token.seat,
                    "timeout_seconds": 30,
                }
            })

            # 启动30秒投票超时
            room._vote_timeout_task = asyncio.create_task(
                _vote_timeout_handler(room.room_code)
            )
            return

    # 非游戏中断线 → 正常移除
    room = room_manager.remove_player(player_id)

    if room:
        logger.info(f"玩家 {player_id} 离开房间 {room.room_code}")

        players_info = [
            {"seat": p.seat, "name": p.player_name, "ready": p.is_ready}
            for p in room.players.values()
        ]
        await broadcast_to_room(room, {
            "type": "player_left",
            "payload": {
                "player_id": player_id,
                "players": players_info,
            }
        })

    # 有人离开（可能房间消失或人数变化），广播大厅更新
    await broadcast_lobby_update()


async def handle_reconnect_vote(websocket: WebSocket, player_id: str, payload: dict):
    """处理重连投票"""
    room = room_manager.get_room_by_player(player_id)
    if not room or not room.game_paused:
        await send_json(websocket, {"type": "error", "payload": {"message": "当前无需投票"}})
        return

    choice = payload.get("choice", "")
    if choice not in ("end", "continue"):
        await send_json(websocket, {"type": "error", "payload": {"message": "无效的投票选项"}})
        return

    room.vote_state[player_id] = choice
    logger.info(f"房间 {room.room_code} 玩家 {player_id} 投票: {choice}")

    # 广播投票更新
    await broadcast_to_room(room, {
        "type": "vote_update",
        "payload": {
            "player_id": player_id,
            "choice": choice,
            "votes": {pid: c for pid, c in room.vote_state.items()},
        }
    })

    # 有人选"结束" → 立即结束游戏
    if choice == "end":
        await _end_game_by_disconnect(room)
        return

    # 所有在线玩家都选了"继续" → 启动60秒重连等待
    online_ids = set(room.players.keys())
    all_voted_continue = all(
        room.vote_state.get(pid) == "continue" for pid in online_ids
    )
    if all_voted_continue and len(online_ids) >= 2:
        # 取消投票超时
        if room._vote_timeout_task and not room._vote_timeout_task.done():
            room._vote_timeout_task.cancel()
            room._vote_timeout_task = None

        await broadcast_to_room(room, {
            "type": "reconnect_waiting",
            "payload": {"timeout_seconds": 60}
        })

        # 启动60秒重连超时
        room._reconnect_timeout_task = asyncio.create_task(
            _reconnect_timeout_handler(room.room_code)
        )


async def _vote_timeout_handler(room_code: str):
    """30秒无人投票 → 自动结束游戏"""
    await asyncio.sleep(30)
    room = room_manager.get_room(room_code)
    if room and room.game_paused and not room.vote_state:
        logger.info(f"房间 {room_code} 投票超时，自动结束游戏")
        await _end_game_by_disconnect(room)


async def _reconnect_timeout_handler(room_code: str):
    """60秒重连超时 → 结束游戏"""
    await asyncio.sleep(60)
    room = room_manager.get_room(room_code)
    if room and room.game_paused:
        logger.info(f"房间 {room_code} 重连超时，结束游戏")
        await _end_game_by_disconnect(room)


async def _end_game_by_disconnect(room: Room):
    """因断线结束游戏"""
    # 取消所有待处理的定时任务
    if room._vote_timeout_task and not room._vote_timeout_task.done():
        room._vote_timeout_task.cancel()
        room._vote_timeout_task = None
    if room._reconnect_timeout_task and not room._reconnect_timeout_task.done():
        room._reconnect_timeout_task.cancel()
        room._reconnect_timeout_task = None

    room.game_paused = False
    room.vote_state.clear()
    # 注意：不清除 reconnect_tokens，让断线玩家重连时能收到 game_ended 通知

    # 通知所有在线玩家游戏结束
    await broadcast_to_room(room, {
        "type": "game_ended",
        "payload": {"reason": "player_disconnected"}
    })

    # 重置游戏状态（但保留 reconnect_tokens）
    room.game_started = False
    room.game = None
    server_config.on_game_end()


# ========== WebSocket 端点 ==========

@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket, reconnect_player_id: str = Query(default=None)):
    await websocket.accept()
    player_id = uuid4().hex[:8]
    connected_websockets.add(websocket)
    logger.info(f"新连接: {player_id} (reconnect_id={reconnect_player_id})")

    # 尝试断线重连
    if reconnect_player_id:
        result = room_manager.try_reconnect(reconnect_player_id, websocket)
        if result:
            room, token = result
            player_id = reconnect_player_id
            logger.info(f"玩家 {token.player_name} (座位:{token.seat}) 重连成功")

            # 检查游戏是否已经结束
            if room.game:
                # 游戏仍在进行中 → 发送重连状态
                reconnect_state = room.game.get_reconnect_state(token.seat)
                await send_json(websocket, {
                    "type": "reconnected",
                    "payload": {
                        "room_code": room.room_code,
                        "player_id": player_id,
                        "seat": token.seat,
                        "game_state": reconnect_state,
                    }
                })
            else:
                # 游戏已结束 → 通知客户端
                await send_json(websocket, {
                    "type": "game_ended",
                    "payload": {"reason": "player_disconnected"}
                })

            # 取消待处理的定时任务
            if room._vote_timeout_task and not room._vote_timeout_task.done():
                room._vote_timeout_task.cancel()
                room._vote_timeout_task = None
            if room._reconnect_timeout_task and not room._reconnect_timeout_task.done():
                room._reconnect_timeout_task.cancel()
                room._reconnect_timeout_task = None

            # 通知其他玩家
            await broadcast_to_room(room, {
                "type": "player_reconnected",
                "payload": {
                    "player_id": player_id,
                    "player_name": token.player_name,
                    "seat": token.seat,
                }
            }, exclude_player=player_id)

            # 标记已重连（防止旧连接 disconnect 误触）
            _reconnected_player_ids.add(player_id)
        else:
            # 重连失败（令牌过期或不存在），作为新连接处理
            logger.info(f"重连失败 (id={reconnect_player_id})，作为新连接处理")

    try:
        while True:
            data = await websocket.receive_json()
            message = ClientMessage(**data)
            msg_type = message.type
            payload = message.payload

            logger.info(f"[{player_id}] 收到: {msg_type}")

            if msg_type == "create_room":
                await handle_create_room(websocket, player_id, payload)
            elif msg_type == "join_room":
                await handle_join_room(websocket, player_id, payload)
            elif msg_type == "ready":
                await handle_ready(websocket, player_id, payload)
            elif msg_type == "cancel_ready":
                await handle_cancel_ready(websocket, player_id, payload)
            elif msg_type == "dealing_complete":
                await handle_dealing_complete(player_id)
            elif msg_type == "bid":
                await handle_bid(websocket, player_id, payload)
            elif msg_type == "play":
                await handle_play(websocket, player_id, payload)
            elif msg_type == "pass":
                await handle_pass(websocket, player_id, payload)
            elif msg_type == "list_rooms":
                await handle_list_rooms(websocket)
            elif msg_type == "set_room_visibility":
                await handle_set_room_visibility(websocket, player_id, payload)
            elif msg_type == "reconnect_vote":
                await handle_reconnect_vote(websocket, player_id, payload)
            else:
                await send_json(websocket, {
                    "type": "error",
                    "payload": {"message": f"未知消息类型: {msg_type}"}
                })

    except WebSocketDisconnect:
        logger.info(f"断开连接: {player_id}")
        await handle_disconnect(player_id, websocket)
    except Exception as e:
        logger.error(f"异常 [{player_id}]: {e}")
        await handle_disconnect(player_id, websocket)
    finally:
        connected_websockets.discard(websocket)


# ========== HTTP 端点 ==========

@app.get("/")
async def root():
    return {
        "message": "萝莉丝扑克 - 斗地主服务器",
        "version": "1.0.0",
        **server_config.get_status(),
    }


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.get("/stats")
async def stats():
    return server_config.get_status()


if __name__ == "__main__":
    import os
    import uvicorn

    # 环境变量覆盖配置文件，配置文件提供默认值
    port = int(os.environ.get("SERVER_PORT") or server_config.port or 8050)
    master_url = os.environ.get("MASTER_URL") or server_config.master_url or None
    slave_name = os.environ.get("SLAVE_NAME") or server_config.slave_name
    slave_host = os.environ.get("SLAVE_HOST") or server_config.slave_host
    # 注册到列表服务器时通告的端口（0=使用监听端口，环境变量 SLAVE_PORT 覆盖）
    slave_port = int(os.environ.get("SLAVE_PORT", str(server_config.slave_port))) or port

    if master_url:
        # 从服务器模式：启动后注册到列表服务器
        from slave_config import SlaveConfig, SlaveRegistration

        _slave_config = SlaveConfig(
            server_name=slave_name,
            host=slave_host,
            port=slave_port,
            max_concurrent_games=server_config.max_concurrent_games,
        )
        _slave_reg = SlaveRegistration(
            master_url=master_url,
            config=_slave_config,
            get_stats=lambda: server_config.get_status(),
        )

        @app.on_event("startup")
        async def _start_registration():
            await _slave_reg.start()
            logger.info(f"从服务器模式: 注册到 {master_url}")

        @app.on_event("shutdown")
        async def _stop_registration():
            await _slave_reg.stop()

    uvicorn.run(app, host="0.0.0.0", port=port)
