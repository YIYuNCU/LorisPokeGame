"""
game_logic.py - 服务端游戏状态机
管理斗地主游戏流程：发牌 → 叫地主 → 出牌 → 胜负判定
"""

from card_models import (
    Card, CardCombo, CardComboType,
    Rank, Suit, classify_play, can_beat, deal_cards, sort_hand
)
from dataclasses import dataclass, field
from typing import Optional
import random


# GamePhase 值（与 C# 枚举对应）
PHASE_IDLE = 0
PHASE_DEALING = 1
PHASE_BIDDING = 2
PHASE_PLAYING = 3
PHASE_GAMEOVER = 4


@dataclass
class GameAction:
    """游戏操作结果（发送给客户端的消息列表）"""
    messages: list[dict] = field(default_factory=list)


@dataclass
class PlayerState:
    """服务端玩家状态"""
    seat: int
    name: str
    hand: list[Card] = field(default_factory=list)
    role: str = "farmer"  # "farmer" or "landlord"


class ServerGameManager:
    """
    服务端游戏管理器（与 C# GameManager 逻辑一致）
    不使用事件，而是返回消息列表
    """

    def __init__(self):
        self.players: list[PlayerState] = [
            PlayerState(seat=0, name=""),
            PlayerState(seat=1, name=""),
            PlayerState(seat=2, name=""),
        ]
        self.kitty_cards: list[Card] = []
        self.phase: int = PHASE_IDLE
        self.current_player: int = 0
        self.last_played_combo: Optional[CardCombo] = None
        self.last_played_by: Optional[int] = None
        self.consecutive_passes: int = 0
        self.current_bid: int = 0
        self.highest_bidder: Optional[int] = None
        self.bid_round: int = 0
        self.multiplier: int = 1
        self.landlord_index: Optional[int] = None

    def set_player_name(self, seat: int, name: str):
        self.players[seat].name = name

    def start_game(self) -> dict:
        """
        开始新游戏（发牌+叫地主阶段）
        返回: { hand_per_player: [[card_dict,...],...], kitty: [...], first_bidder: int }
        """
        # 重置状态
        self.last_played_combo = None
        self.last_played_by = None
        self.consecutive_passes = 0
        self.current_bid = 0
        self.highest_bidder = None
        self.landlord_index = None
        self.bid_round = 0
        self.multiplier = 1

        for p in self.players:
            p.hand = []
            p.role = "farmer"

        # 发牌
        h0, h1, h2, kitty = deal_cards()
        self.players[0].hand = h0
        self.players[1].hand = h1
        self.players[2].hand = h2
        self.kitty_cards = kitty

        # 随机选第一个叫地主的人
        self.current_player = random.randint(0, 2)
        self.phase = PHASE_BIDDING

        return {
            "hands": [
                [c.to_dict() for c in p.hand] for p in self.players
            ],
            "kitty": [c.to_dict() for c in self.kitty_cards],
            "first_bidder": self.current_player,
        }

    def submit_bid(self, seat: int, amount: int) -> GameAction:
        """
        提交叫分
        amount: 0=不叫, 1~3=叫分
        返回: 需要发送的消息列表
        """
        result = GameAction()

        if self.phase != PHASE_BIDDING or seat != self.current_player:
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "不是你的叫分回合"}})
            return result

        # 叫分范围校验：0=不叫，1~3=叫分
        if amount < 0 or amount > 3:
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "叫分范围应为 0-3"}})
            return result

        self.bid_round += 1

        if amount > self.current_bid:
            self.current_bid = amount
            self.highest_bidder = seat
            result.messages.append({
                "type": "bid_update",
                "broadcast": True,
                "payload": {"player_seat": seat, "amount": amount, "player_name": self.players[seat].name}
            })

            # 叫了3分，立即分配地主
            if amount >= 3:
                result.messages.extend(self._assign_landlord(seat))
                return result
        else:
            # 不叫
            result.messages.append({
                "type": "bid_update",
                "broadcast": True,
                "payload": {"player_seat": seat, "amount": 0, "player_name": self.players[seat].name}
            })

        # 所有玩家都叫过
        if self.bid_round >= 3:
            if self.highest_bidder is not None:
                result.messages.extend(self._assign_landlord(self.highest_bidder))
            else:
                # 无人叫地主，重新发牌
                game_data = self.start_game()
                result.messages.append({
                    "type": "game_restart",
                    "broadcast": True,
                    "payload": {"message": "无人叫地主，重新发牌"}
                })
                # 构建玩家名称列表（按座位顺序），与 main.py start_game_in_room 一致
                names_by_seat = [self.players[s].name for s in range(3)]
                # 重新发送手牌给各玩家
                for s in range(3):
                    result.messages.append({
                        "type": "game_start",
                        "target": s,
                        "payload": {
                            "hand": game_data["hands"][s],
                            "kitty_count": 3,
                            "first_bidder": game_data["first_bidder"],
                            "player_names": names_by_seat,
                        }
                    })
                # 通知第一个叫分的人（修复卡死：客户端依赖此消息触发叫分UI）
                result.messages.append({
                    "type": "turn_change",
                    "broadcast": True,
                    "payload": {
                        "current_player": game_data["first_bidder"],
                        "phase": "bidding"
                    }
                })
            return result

        # 下一位叫分
        self.current_player = (self.current_player + 1) % 3
        result.messages.append({
            "type": "turn_change",
            "broadcast": True,
            "payload": {
                "current_player": self.current_player,
                "phase": "bidding"
            }
        })

        return result

    def _assign_landlord(self, seat: int) -> list[dict]:
        """分配地主，返回消息列表"""
        messages = []
        self.landlord_index = seat
        self.players[seat].role = "landlord"
        self.players[seat].hand.extend(self.kitty_cards)
        self.players[seat].hand = sort_hand(self.players[seat].hand)
        self.multiplier = max(self.current_bid, 1)

        messages.append({
            "type": "landlord_assigned",
            "broadcast": True,
            "payload": {
                "seat": seat,
                "kitty": [c.to_dict() for c in self.kitty_cards],
                "multiplier": self.multiplier,
                "player_name": self.players[seat].name,
            }
        })

        # 地主先出
        self.current_player = seat
        self.phase = PHASE_PLAYING
        messages.append({
            "type": "turn_change",
            "broadcast": True,
            "payload": {
                "current_player": seat,
                "phase": "playing"
            }
        })

        return messages

    def submit_play(self, seat: int, cards: list[Card]) -> GameAction:
        """
        出牌
        返回: 需要发送的消息列表
        """
        result = GameAction()

        if self.phase != PHASE_PLAYING or seat != self.current_player:
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "不是你的出牌回合"}})
            return result

        combo = classify_play(cards)
        if not combo.is_valid:
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "无效牌型"}})
            return result

        # 手牌所有权 + 重复牌校验
        hand_set = {(c.suit, c.rank) for c in self.players[seat].hand}
        requested_set = {(c.suit, c.rank) for c in cards}
        if len(cards) != len(requested_set):
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "出牌包含重复牌"}})
            return result
        if not requested_set.issubset(hand_set):
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "出牌不在手中"}})
            return result

        # 需要压过上家
        if self.last_played_combo is not None and self.last_played_by != seat:
            if not can_beat(combo, self.last_played_combo):
                result.messages.append({"type": "error", "target": seat,
                                        "payload": {"message": "牌型无法压过上家"}})
                return result

        # 炸弹/火箭翻倍
        if combo.type in (CardComboType.Bomb, CardComboType.Rocket):
            self.multiplier *= 2

        # 从手牌中移除
        to_remove = {(c.suit, c.rank) for c in cards}
        self.players[seat].hand = [c for c in self.players[seat].hand if (c.suit, c.rank) not in to_remove]

        self.last_played_combo = combo
        self.last_played_by = seat
        self.consecutive_passes = 0

        result.messages.append({
            "type": "cards_played",
            "broadcast": True,
            "payload": {
                "seat": seat,
                "cards": [c.to_dict() for c in cards],
                "card_count": len(self.players[seat].hand),
                "combo_type": combo.type,
            }
        })

        # 检查是否出完
        if len(self.players[seat].hand) == 0:
            self.phase = PHASE_GAMEOVER
            winner_role = self.players[seat].role
            result.messages.append({
                "type": "game_over",
                "broadcast": True,
                "payload": {
                    "winner_seat": seat,
                    "winner_role": winner_role,
                    "multiplier": self.multiplier,
                }
            })
            return result

        # 下一位出牌
        self._move_to_next()
        result.messages.append({
            "type": "turn_change",
            "broadcast": True,
            "payload": {
                "current_player": self.current_player,
                "phase": "playing"
            }
        })

        return result

    def submit_pass(self, seat: int) -> GameAction:
        """不出"""
        result = GameAction()

        if self.phase != PHASE_PLAYING or seat != self.current_player:
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "不是你的回合"}})
            return result

        # 首出不能跳过
        if self.last_played_combo is None or self.last_played_by == seat:
            result.messages.append({"type": "error", "target": seat,
                                    "payload": {"message": "你必须出牌"}})
            return result

        self.consecutive_passes += 1

        result.messages.append({
            "type": "player_passed",
            "broadcast": True,
            "payload": {"seat": seat}
        })

        # 连续两人不出，清空上家出牌
        if self.consecutive_passes >= 2:
            self.last_played_combo = None
            self.last_played_by = None
            self.consecutive_passes = 0

        self._move_to_next()
        result.messages.append({
            "type": "turn_change",
            "broadcast": True,
            "payload": {
                "current_player": self.current_player,
                "phase": "playing"
            }
        })

        return result

    def _move_to_next(self):
        self.current_player = (self.current_player + 1) % 3

    def get_hand(self, seat: int) -> list[dict]:
        """获取指定玩家的手牌（用于发送给该玩家）"""
        return [c.to_dict() for c in self.players[seat].hand]

    def get_public_state(self) -> dict:
        """获取公共可见状态"""
        return {
            "phase": self.phase,
            "current_player": self.current_player,
            "card_counts": [len(p.hand) for p in self.players],
            "landlord_index": self.landlord_index,
            "multiplier": self.multiplier,
            "last_played_by": self.last_played_by,
        }

    def get_reconnect_state(self, seat: int) -> dict:
        """获取重连玩家所需的游戏状态"""
        opponent_counts = []
        opponent_seats = []
        for s in range(3):
            if s != seat:
                opponent_counts.append(len(self.players[s].hand))
                opponent_seats.append(s)

        last_played_cards = []
        if self.last_played_combo and self.last_played_combo.cards:
            last_played_cards = [c.to_dict() for c in self.last_played_combo.cards]

        names_by_seat = [self.players[s].name for s in range(3)]

        return {
            "hand": self.get_hand(seat),
            "opponent_counts": opponent_counts,
            "opponent_seats": opponent_seats,
            "current_player": self.current_player,
            "phase": "bidding" if self.phase == PHASE_BIDDING else "playing",
            "landlord_seat": self.landlord_index,
            "kitty_count": len(self.kitty_cards),
            "multiplier": self.multiplier,
            "last_played_cards": last_played_cards,
            "last_played_by": self.last_played_by,
            "player_names": names_by_seat,
            "card_counts": [len(p.hand) for p in self.players],
        }
