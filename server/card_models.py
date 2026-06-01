"""
card_models.py - 扑克牌模型和规则引擎（Python 移植版）
与 C# LolitaPoker.Core 的 RulesEngine 完全一致
"""

from enum import IntEnum
from dataclasses import dataclass
from collections import Counter
from typing import Optional
import random


class Suit(IntEnum):
    """花色枚举（与 C# Suit 对应）"""
    Diamonds = 0   # 方片
    Spades = 1     # 黑桃
    Hearts = 2     # 红桃
    Clubs = 3      # 梅花
    None_ = 4      # 无花色（王牌）


class Rank(IntEnum):
    """点数枚举（与 C# Rank 对应，数值=游戏强度）"""
    Three = 3
    Four = 4
    Five = 5
    Six = 6
    Seven = 7
    Eight = 8
    Nine = 9
    Ten = 10
    Jack = 11
    Queen = 12
    King = 13
    Ace = 14
    Two = 15
    SmallJoker = 16
    BigJoker = 17


class CardComboType(IntEnum):
    """牌型枚举（与 C# CardComboType 对应）"""
    None_ = 0
    Single = 1
    Pair = 2
    Triple = 3
    TriplePlusOne = 4
    TriplePlusPair = 5
    Straight = 6
    ConsecutivePairs = 7
    Airplane = 8
    AirplaneWithSingles = 9
    AirplaneWithPairs = 10
    FourPlusTwo = 11
    FourPlusTwoPairs = 12
    Bomb = 13
    Rocket = 14


@dataclass(frozen=True)
class Card:
    """扑克牌（与 C# Card 对应）"""
    suit: int   # Suit 枚举值
    rank: int   # Rank 枚举值

    @property
    def strength(self) -> int:
        return self.rank

    @property
    def is_joker(self) -> bool:
        return self.suit == Suit.None_

    def to_dict(self) -> dict:
        return {"suit": self.suit, "rank": self.rank}

    @staticmethod
    def from_dict(d: dict) -> "Card":
        return Card(suit=d["suit"], rank=d["rank"])


@dataclass
class CardCombo:
    """牌型组合（与 C# CardCombo 对应）"""
    type: int          # CardComboType 枚举值
    primary_rank: int  # 主牌点数
    chain_length: int  # 链长度
    cards: list[Card]  # 包含的牌

    @property
    def is_valid(self) -> bool:
        return self.type != CardComboType.None_

    @staticmethod
    def invalid() -> "CardCombo":
        return CardCombo(CardComboType.None_, Rank.Three, 0, [])


def create_full_deck() -> list[Card]:
    """创建一副完整的54张牌（与 C# CardHelper.CreateFullDeck 一致）"""
    deck = []
    for suit in [Suit.Diamonds, Suit.Spades, Suit.Hearts, Suit.Clubs]:
        for rank in range(Rank.Three, Rank.Two + 1):  # 3~15 (3~2)
            deck.append(Card(suit=suit, rank=rank))
    # 大小王
    deck.append(Card(suit=Suit.None_, rank=Rank.SmallJoker))
    deck.append(Card(suit=Suit.None_, rank=Rank.BigJoker))
    return deck


def sort_hand(hand: list[Card]) -> list[Card]:
    """按斗地主规则排序（大的在前，同点数按花色）"""
    return sorted(hand, key=lambda c: (-c.strength, c.suit))


def deal_cards() -> tuple[list[Card], list[Card], list[Card], list[Card]]:
    """洗牌并发牌：3个玩家各17张，3张底牌（与 C# Deck.Deal 一致）"""
    deck = create_full_deck()
    random.shuffle(deck)

    hand0, hand1, hand2, kitty = [], [], [], []
    for i in range(51):
        if i % 3 == 0:
            hand0.append(deck[i])
        elif i % 3 == 1:
            hand1.append(deck[i])
        else:
            hand2.append(deck[i])

    kitty = [deck[51], deck[52], deck[53]]

    return sort_hand(hand0), sort_hand(hand1), sort_hand(hand2), sort_hand(kitty)


# ========== 规则引擎（与 C# RulesEngine 完全对应） ==========

def _can_form_chain(sorted_ranks: list[int]) -> bool:
    """判断一组点数是否能形成连续序列（3~A范围，不含2和王）"""
    if len(sorted_ranks) < 2:
        return False

    for rank in sorted_ranks:
        if rank in (Rank.Two, Rank.SmallJoker, Rank.BigJoker):
            return False

    for i in range(1, len(sorted_ranks)):
        if sorted_ranks[i] - sorted_ranks[i - 1] != 1:
            return False

    return True


def classify_play(cards: list[Card]) -> CardCombo:
    """识别牌型（与 C# RulesEngine.ClassifyPlay 一致）"""
    if not cards:
        return CardCombo.invalid()

    # 按点数统计频率
    freq: dict[int, int] = Counter(c.rank for c in cards)

    # 按频率分组
    counts: dict[int, list[int]] = {}
    for rank, count in freq.items():
        counts.setdefault(count, []).append(rank)

    total_cards = len(cards)
    sorted_ranks = sorted(freq.keys())

    # 火箭：大小王
    if total_cards == 2 and Rank.SmallJoker in freq and Rank.BigJoker in freq:
        return CardCombo(CardComboType.Rocket, Rank.BigJoker, 1, list(cards))

    # 炸弹：四张相同
    if len(freq) == 1 and list(freq.values())[0] == 4:
        return CardCombo(CardComboType.Bomb, list(freq.keys())[0], 1, list(cards))

    # 单张/对子/三条
    if len(freq) == 1:
        rank = list(freq.keys())[0]
        count = list(freq.values())[0]
        if count == 1:
            return CardCombo(CardComboType.Single, rank, 1, list(cards))
        elif count == 2:
            return CardCombo(CardComboType.Pair, rank, 1, list(cards))
        elif count == 3:
            return CardCombo(CardComboType.Triple, rank, 1, list(cards))
        else:
            return CardCombo.invalid()

    # 三带一/三带二
    if 3 in counts and len(counts[3]) == 1:
        triple_rank = counts[3][0]
        if 1 in counts and len(counts[1]) == 1 and total_cards == 4:
            return CardCombo(CardComboType.TriplePlusOne, triple_rank, 1, list(cards))
        if 2 in counts and len(counts[2]) == 1 and total_cards == 5:
            return CardCombo(CardComboType.TriplePlusPair, triple_rank, 1, list(cards))

    # 四带二
    if 4 in counts and len(counts[4]) == 1:
        four_rank = counts[4][0]
        if 1 in counts and len(counts[1]) == 2 and total_cards == 6:
            return CardCombo(CardComboType.FourPlusTwo, four_rank, 1, list(cards))
        if 2 in counts and len(counts[2]) == 2 and total_cards == 8:
            return CardCombo(CardComboType.FourPlusTwoPairs, four_rank, 1, list(cards))

    # 顺子（5+张连续单张）
    if all(v == 1 for v in freq.values()) and total_cards >= 5 and _can_form_chain(sorted_ranks):
        max_rank = max(sorted_ranks)
        return CardCombo(CardComboType.Straight, max_rank, total_cards, list(cards))

    # 连对（3+组连续对子）
    if all(v == 2 for v in freq.values()) and len(freq) >= 3 and _can_form_chain(sorted_ranks):
        max_rank = max(sorted_ranks)
        return CardCombo(CardComboType.ConsecutivePairs, max_rank, len(freq), list(cards))

    # 飞机（2+组连续三条）
    if 3 in counts and len(counts[3]) >= 2:
        triple_ranks = sorted(counts[3])
        if _can_form_chain(triple_ranks):
            max_rank = max(triple_ranks)
            triple_count = len(triple_ranks)
            non_triple_cards = total_cards - triple_count * 3

            if non_triple_cards == 0:
                return CardCombo(CardComboType.Airplane, max_rank, triple_count, list(cards))
            if non_triple_cards == triple_count and 1 in counts and len(counts[1]) == triple_count:
                return CardCombo(CardComboType.AirplaneWithSingles, max_rank, triple_count, list(cards))
            if non_triple_cards == triple_count * 2 and 2 in counts and len(counts[2]) == triple_count:
                return CardCombo(CardComboType.AirplaneWithPairs, max_rank, triple_count, list(cards))

    return CardCombo.invalid()


def can_beat(candidate: CardCombo, current: CardCombo) -> bool:
    """判断 candidate 能否压过 current（与 C# RulesEngine.CanBeat 一致）"""
    if not candidate.is_valid or not current.is_valid:
        return False

    # 火箭压一切
    if candidate.type == CardComboType.Rocket:
        return True

    # 炸弹压非炸弹/火箭
    if candidate.type == CardComboType.Bomb:
        if current.type == CardComboType.Rocket:
            return False
        if current.type == CardComboType.Bomb:
            return candidate.primary_rank > current.primary_rank
        return True

    # 同类型比较
    if candidate.type != current.type:
        return False
    if candidate.chain_length != current.chain_length:
        return False

    return candidate.primary_rank > current.primary_rank
