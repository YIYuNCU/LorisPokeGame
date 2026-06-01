"""
test_anti_cheat.py - 反作弊测试（防护验证 + 漏洞暴露/金丝雀测试）
"""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import pytest
from card_models import Card, Suit, Rank, CardComboType, classify_play, can_beat, create_full_deck
from game_logic import ServerGameManager, PHASE_BIDDING, PHASE_PLAYING, PHASE_GAMEOVER


def _c(suit, rank):
    """Card 创建快捷方法。"""
    return Card(suit=suit, rank=rank)


def _start_quick_game():
    """开始游戏并立即叫 3 分分配地主，返回 (gm, landlord_seat)。"""
    gm = ServerGameManager()
    gm.start_game()
    first = gm.current_player
    action = gm.submit_bid(first, 3)
    assert gm.phase == PHASE_PLAYING
    return gm, first


# ================================================================
# A. 现有防护验证（应通过）
# ================================================================

class TestExistingProtections:

    @pytest.mark.anti_cheat
    def test_submit_play_rejects_out_of_turn(self):
        gm, landlord = _start_quick_game()
        wrong_seat = (gm.current_player + 1) % 3
        hand = [Card.from_dict(cd) for cd in gm.get_hand(wrong_seat)]
        if len(hand) == 0:
            return

        action = gm.submit_play(wrong_seat, [hand[-1]])
        assert any(m.get("type") == "error" for m in action.messages), \
            "非当前回合出牌应返回错误"

    @pytest.mark.anti_cheat
    def test_submit_play_rejects_invalid_combo(self):
        gm, landlord = _start_quick_game()
        cp = gm.current_player

        # 3 张不构成合法牌型的牌
        invalid = [_c(Suit.Diamonds, Rank.Three),
                   _c(Suit.Hearts, Rank.Seven),
                   _c(Suit.Spades, Rank.Ace)]
        combo = classify_play(invalid)
        if combo.is_valid:
            return  # 恰好合法则跳过

        action = gm.submit_play(cp, invalid)
        assert any(m.get("type") == "error" for m in action.messages), \
            "非法牌型应返回错误"

    @pytest.mark.anti_cheat
    def test_submit_play_rejects_cannot_beat(self):
        gm, landlord = _start_quick_game()

        # 当前玩家出一张最小的牌
        cp = gm.current_player
        hand = [Card.from_dict(cd) for cd in gm.get_hand(cp)]
        if len(hand) == 0:
            return
        smallest = [hand[-1]]
        gm.submit_play(cp, smallest)

        # 下一个玩家尝试出一张更小的
        cp2 = gm.current_player
        hand2 = [Card.from_dict(cd) for cd in gm.get_hand(cp2)]
        smaller = [c for c in hand2 if c.rank < smallest[0].rank]
        if len(smaller) == 0:
            return  # 没有更小的牌

        action = gm.submit_play(cp2, [smaller[0]])
        assert any(m.get("type") == "error" for m in action.messages), \
            "无法压过应返回错误"

    @pytest.mark.anti_cheat
    def test_submit_pass_rejected_out_of_turn(self):
        gm, landlord = _start_quick_game()
        wrong_seat = (gm.current_player + 1) % 3

        action = gm.submit_pass(wrong_seat)
        assert any(m.get("type") == "error" for m in action.messages), \
            "非当前回合跳过应返回错误"

    @pytest.mark.anti_cheat
    def test_submit_pass_rejected_when_leading(self):
        gm, landlord = _start_quick_game()
        cp = gm.current_player

        action = gm.submit_pass(cp)
        assert any(m.get("type") == "error" for m in action.messages), \
            "自由出牌时跳过应返回错误"

    @pytest.mark.anti_cheat
    def test_submit_bid_rejected_out_of_turn(self):
        gm = ServerGameManager()
        gm.start_game()
        wrong_seat = (gm.current_player + 1) % 3

        action = gm.submit_bid(wrong_seat, 1)
        assert any(m.get("type") == "error" for m in action.messages), \
            "非当前回合叫分应返回错误"

    @pytest.mark.anti_cheat
    def test_submit_bid_rejected_wrong_phase(self):
        gm, landlord = _start_quick_game()
        assert gm.phase == PHASE_PLAYING

        action = gm.submit_bid(gm.current_player, 1)
        assert any(m.get("type") == "error" for m in action.messages), \
            "非叫分阶段叫分应返回错误"


# ================================================================
# B. 漏洞验证/金丝雀测试
# ================================================================

class TestAntiCheatVulnerabilities:

    @pytest.mark.anti_cheat
    def test_submit_play_duplicate_cards_bomb_exploit_rejected(self):
        """修复验证：重复牌伪装炸弹现在应被拒绝"""
        gm, landlord = _start_quick_game()
        cp = gm.current_player
        hand = [Card.from_dict(cd) for cd in gm.get_hand(cp)]
        if len(hand) == 0:
            return

        real_card = hand[-1]
        hand_before = len(hand)

        # 提交同一张牌 4 次——伪装炸弹，应被拒绝
        action = gm.submit_play(cp, [real_card, real_card, real_card, real_card])

        # 修复后：出牌被拒绝
        error_msgs = [m for m in action.messages if m.get("type") == "error"]
        assert len(error_msgs) > 0, "重复牌出牌应被拒绝"
        assert "重复牌" in error_msgs[0]["payload"]["message"]

    @pytest.mark.anti_cheat
    def test_submit_play_cards_not_in_hand_rejected(self):
        """修复验证：提交手中没有的牌应被拒绝"""
        gm, landlord = _start_quick_game()
        cp = gm.current_player
        hand = [Card.from_dict(cd) for cd in gm.get_hand(cp)]
        hand_set = {(c.suit, c.rank) for c in hand}

        # 找一张不在手中的牌
        full_deck = create_full_deck()
        not_in_hand = [c for c in full_deck if (c.suit, c.rank) not in hand_set]
        if len(not_in_hand) == 0:
            return

        fake_card = not_in_hand[0]
        action = gm.submit_play(cp, [fake_card])

        # 修复后：出牌被拒绝
        error_msgs = [m for m in action.messages if m.get("type") == "error"]
        assert len(error_msgs) > 0, "不在手中的牌出牌应被拒绝"
        assert "不在手中" in error_msgs[0]["payload"]["message"]

    @pytest.mark.anti_cheat
    def test_bid_amount_huge_value_rejected(self):
        """修复验证：叫分超过 3 应被拒绝"""
        gm = ServerGameManager()
        gm.start_game()
        cp = gm.current_player

        action = gm.submit_bid(cp, 999999)

        # 修复后：叫分被拒绝
        error_msgs = [m for m in action.messages if m.get("type") == "error"]
        assert len(error_msgs) > 0, "超大叫分应被拒绝"
        assert gm.phase == PHASE_BIDDING, "应仍在叫分阶段"

    @pytest.mark.anti_cheat
    def test_bid_amount_negative_rejected(self):
        """修复验证：负数叫分应被拒绝"""
        gm = ServerGameManager()
        gm.start_game()
        cp = gm.current_player

        action = gm.submit_bid(cp, -5)

        error_msgs = [m for m in action.messages if m.get("type") == "error"]
        assert len(error_msgs) > 0, "负数叫分应被拒绝"
        assert gm.phase == PHASE_BIDDING

    @pytest.mark.anti_cheat
    def test_bid_amount_valid_3_assigns_landlord(self):
        """验证：叫 3 分应正常分配地主"""
        gm = ServerGameManager()
        gm.start_game()
        cp = gm.current_player

        action = gm.submit_bid(cp, 3)
        assert gm.phase == PHASE_PLAYING
        assert gm.landlord_index == cp
        assert gm.multiplier == 3

    @pytest.mark.anti_cheat
    def test_card_suit_rank_range_validated(self):
        """修复验证：极端牌值现在应被 Pydantic 拒绝"""
        from pydantic import ValidationError
        from models import CardData

        # 超范围花色
        with pytest.raises(ValidationError):
            CardData(suit=99, rank=3)

        # 超范围点数
        with pytest.raises(ValidationError):
            CardData(suit=0, rank=999)

        # 合法值应正常创建
        data = CardData(suit=0, rank=3)
        assert data.suit == 0
        assert data.rank == 3


# ================================================================
# C. 模型层验证
# ================================================================

class TestModelValidation:

    @pytest.mark.anti_cheat
    def test_bid_payload_rejects_negative_amount(self):
        """修复验证：BidPayload 拒绝负数叫分"""
        from models import BidPayload
        from pydantic import ValidationError
        with pytest.raises(ValidationError):
            BidPayload(amount=-5)

    @pytest.mark.anti_cheat
    def test_bid_payload_rejects_huge_amount(self):
        """修复验证：BidPayload 拒绝超大叫分"""
        from models import BidPayload
        from pydantic import ValidationError
        with pytest.raises(ValidationError):
            BidPayload(amount=999999)

    @pytest.mark.anti_cheat
    def test_bid_payload_accepts_valid_amount(self):
        """验证：合法叫分值正常创建"""
        from models import BidPayload
        for amount in range(0, 4):
            payload = BidPayload(amount=amount)
            assert payload.amount == amount

    @pytest.mark.anti_cheat
    def test_card_data_rejects_invalid_suit(self):
        """修复验证：CardData 拒绝超范围花色"""
        from models import CardData
        from pydantic import ValidationError
        with pytest.raises(ValidationError):
            CardData(suit=99, rank=3)

    @pytest.mark.anti_cheat
    def test_card_data_rejects_invalid_rank(self):
        """修复验证：CardData 拒绝超范围点数"""
        from models import CardData
        from pydantic import ValidationError
        with pytest.raises(ValidationError):
            CardData(suit=0, rank=999)

    @pytest.mark.anti_cheat
    def test_card_data_accepts_valid_range(self):
        """验证：合法牌值正常创建"""
        from models import CardData
        for suit in range(0, 5):
            for rank in range(3, 18):
                data = CardData(suit=suit, rank=rank)
                assert data.suit == suit
                assert data.rank == rank


# ================================================================
# D. 房间管理防护
# ================================================================

class TestRoomManagementProtections:

    @pytest.mark.anti_cheat
    def test_join_room_rejects_already_started(self):
        from room_manager import RoomManager

        class MockWS:
            pass

        rm = RoomManager()
        code, room, _ = rm.create_room("host", "Host", MockWS())
        rm.join_room(code, "p1", "P1", MockWS())
        rm.join_room(code, "p2", "P2", MockWS())

        # 模拟游戏开始
        room.game_started = True

        # 新玩家应无法加入已开始的游戏
        success, msg, seat = rm.join_room(code, "late", "Late", MockWS())
        assert not success, "已开始的游戏应拒绝新玩家加入"

    @pytest.mark.anti_cheat
    def test_join_room_rejects_full_room(self):
        from room_manager import RoomManager

        class MockWS:
            pass

        rm = RoomManager()
        code, room, _ = rm.create_room("host", "Host", MockWS())
        rm.join_room(code, "p1", "P1", MockWS())
        rm.join_room(code, "p2", "P2", MockWS())

        # 房间已满（3 人），新玩家应无法加入
        success, msg, seat = rm.join_room(code, "late", "Late", MockWS())
        assert not success, "满员房间应拒绝新玩家加入"
