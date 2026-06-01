"""
test_stress_performance.py - 服务端压力测试、性能测试和内存管理测试
"""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import gc
import re
import time
import pytest
from card_models import Card, Suit, Rank, CardComboType, classify_play, can_beat, deal_cards, create_full_deck, sort_hand
from game_logic import ServerGameManager, PHASE_BIDDING, PHASE_PLAYING, PHASE_GAMEOVER
from room_manager import RoomManager


class MockWebSocket:
    """最小化 WebSocket 桩对象，仅用于身份标识。"""
    pass


def _c(suit, rank):
    """Card 创建快捷方法。"""
    return Card(suit=suit, rank=rank)


# ================================================================
# 压力测试: RoomManager
# ================================================================

class TestRoomManagerStress:

    @pytest.mark.stress
    def test_create_1000_rooms_unique_codes(self):
        rm = RoomManager()
        codes = set()
        for i in range(1000):
            code, _, _ = rm.create_room(f"p{i}", f"Player{i}", MockWebSocket())
            codes.add(code)

        assert len(codes) == 1000, "房间码应唯一"
        for code in codes:
            assert re.match(r'^[A-Z0-9]{6}$', code), f"房间码格式不对: {code}"

    @pytest.mark.stress
    def test_rapid_join_leave_10000_cycles(self):
        rm = RoomManager()
        code, room, _ = rm.create_room("host", "Host", MockWebSocket())

        for i in range(10_000):
            success, msg, seat = rm.join_room(code, f"p{i}", f"P{i}", MockWebSocket())
            if success:
                rm.remove_player(f"p{i}")

        # 房间应仍然存在（host 还在）
        assert rm.get_room(code) is not None

    @pytest.mark.stress
    def test_concurrent_room_creation_500_rooms(self):
        rm = RoomManager()
        rooms_info = []

        # 创建 500 个房间，每个加入 3 个玩家
        for i in range(500):
            host_id = f"host_{i}"
            code, room, _ = rm.create_room(host_id, f"Host{i}", MockWebSocket())
            rm.join_room(code, f"p1_{i}", f"P1_{i}", MockWebSocket())
            rm.join_room(code, f"p2_{i}", f"P2_{i}", MockWebSocket())
            rooms_info.append((code, host_id, f"p1_{i}", f"p2_{i}"))

        # 验证所有房间已满
        for code, *_ in rooms_info:
            room = rm.get_room(code)
            assert room is not None
            assert room.is_full

        # 移除所有玩家
        for code, h, p1, p2 in rooms_info:
            rm.remove_player(h)
            rm.remove_player(p1)
            rm.remove_player(p2)

        # 所有房间应被自动清理
        for code, *_ in rooms_info:
            assert rm.get_room(code) is None, f"房间 {code} 应已被清理"

    @pytest.mark.stress
    def test_room_code_collision_resilience(self):
        rm = RoomManager()
        codes = []
        for i in range(5000):
            code, _, _ = rm.create_room(f"p{i}", f"Player{i}", MockWebSocket())
            codes.append(code)

        assert len(set(codes)) == 5000, "5000 个房间码应全部唯一"


# ================================================================
# 压力测试: ServerGameManager
# ================================================================

class TestServerGameManagerStress:

    def _play_one_game(self):
        """用最简 AI 逻辑打一局完整对局，返回回合数。"""
        gm = ServerGameManager()
        gm.start_game()

        # 第一个玩家直接叫 3 分
        first_bidder = gm.current_player
        action = gm.submit_bid(first_bidder, 3)
        assert gm.phase == PHASE_PLAYING

        turns = 0
        max_turns = 500
        while gm.phase != PHASE_GAMEOVER and turns < max_turns:
            cp = gm.current_player
            hand_cards = [Card.from_dict(cd) for cd in gm.get_hand(cp)]
            if len(hand_cards) == 0:
                break

            # 出最小的一张牌
            sorted_hand = sort_hand(hand_cards)
            smallest = [sorted_hand[-1]]
            combo = classify_play(smallest)

            if gm.last_played_combo is None or not gm.last_played_combo.is_valid:
                # 自由出牌
                gm.submit_play(cp, smallest)
            else:
                # 尝试压过
                if can_beat(combo, gm.last_played_combo):
                    gm.submit_play(cp, smallest)
                else:
                    gm.submit_pass(cp)
            turns += 1

        return turns

    @pytest.mark.stress
    def test_1000_full_games_ai_vs_ai(self):
        for game_idx in range(1000):
            turns = self._play_one_game()
            assert turns < 500, f"游戏 {game_idx} 超过 500 回合上限"

    @pytest.mark.stress
    def test_rapid_game_start_stop_5000_cycles(self):
        for _ in range(5000):
            gm = ServerGameManager()
            gm.start_game()

    @pytest.mark.stress
    def test_submit_play_50000_invalid_plays(self):
        gm = ServerGameManager()
        gm.start_game()
        gm.submit_bid(gm.current_player, 3)

        cp = gm.current_player
        for _ in range(50_000):
            action = gm.submit_play(cp, [])
            # 空牌应返回错误
            assert any(m.get("type") == "error" for m in action.messages)

    @pytest.mark.stress
    def test_submit_bid_all_3_scenarios_1000_times(self):
        for _ in range(1000):
            gm = ServerGameManager()
            gm.start_game()
            first = gm.current_player
            gm.submit_bid(first, 3)
            assert gm.phase == PHASE_PLAYING
            assert gm.landlord_index == first
            assert len(gm.players[first].hand) == 20


# ================================================================
# 性能测试: classify_play
# ================================================================

class TestClassifyPlayPerformance:

    @pytest.mark.performance
    def test_classify_play_single_under_01ms(self):
        cards = [_c(Suit.Diamonds, Rank.Ace)]
        # 预热
        classify_play(cards)

        iterations = 10_000
        start = time.perf_counter()
        for _ in range(iterations):
            classify_play(cards)
        elapsed = time.perf_counter() - start

        avg_ms = (elapsed / iterations) * 1000
        assert avg_ms < 0.1, f"单张平均 {avg_ms:.4f}ms，超过 0.1ms 阈值"

    @pytest.mark.performance
    def test_classify_play_all_types_10000_iterations(self):
        combos = [
            [_c(Suit.Diamonds, Rank.Three)],  # Single
            [_c(Suit.Diamonds, Rank.Five), _c(Suit.Hearts, Rank.Five)],  # Pair
            [_c(Suit.Diamonds, Rank.Seven), _c(Suit.Hearts, Rank.Seven), _c(Suit.Spades, Rank.Seven)],  # Triple
            # Bomb
            [_c(Suit.Diamonds, Rank.Jack), _c(Suit.Hearts, Rank.Jack),
             _c(Suit.Spades, Rank.Jack), _c(Suit.Clubs, Rank.Jack)],
            # Rocket
            [_c(Suit.None_, Rank.SmallJoker), _c(Suit.None_, Rank.BigJoker)],
            # Straight
            [_c(Suit.Diamonds, Rank.Three), _c(Suit.Hearts, Rank.Four),
             _c(Suit.Spades, Rank.Five), _c(Suit.Diamonds, Rank.Six),
             _c(Suit.Hearts, Rank.Seven)],
        ]

        # 预热
        for combo in combos:
            classify_play(combo)

        iterations = 10_000
        start = time.perf_counter()
        for i in range(iterations):
            classify_play(combos[i % len(combos)])
        elapsed = time.perf_counter() - start

        avg_ms = (elapsed / iterations) * 1000
        assert avg_ms < 0.5, f"混合牌型平均 {avg_ms:.4f}ms，超过 0.5ms 阈值"

    @pytest.mark.performance
    def test_classify_play_complex_airplane_performance(self):
        # 飞机带两张单: 333-444 + 5, 6
        cards = [
            _c(Suit.Diamonds, Rank.Three), _c(Suit.Hearts, Rank.Three),
            _c(Suit.Spades, Rank.Three),
            _c(Suit.Diamonds, Rank.Four), _c(Suit.Hearts, Rank.Four),
            _c(Suit.Spades, Rank.Four),
            _c(Suit.Diamonds, Rank.Five), _c(Suit.Hearts, Rank.Six),
        ]

        # 预热
        classify_play(cards)

        iterations = 10_000
        start = time.perf_counter()
        for _ in range(iterations):
            classify_play(cards)
        elapsed = time.perf_counter() - start

        avg_ms = (elapsed / iterations) * 1000
        assert avg_ms < 0.1, f"飞机平均 {avg_ms:.4f}ms，超过 0.1ms 阈值"


# ================================================================
# 性能测试: can_beat
# ================================================================

class TestCanBeatPerformance:

    @pytest.mark.performance
    def test_can_beat_100000_comparisons_under_1s(self):
        bomb_5 = classify_play([
            _c(Suit.Diamonds, Rank.Five), _c(Suit.Hearts, Rank.Five),
            _c(Suit.Spades, Rank.Five), _c(Suit.Clubs, Rank.Five),
        ])
        bomb_6 = classify_play([
            _c(Suit.Diamonds, Rank.Six), _c(Suit.Hearts, Rank.Six),
            _c(Suit.Spades, Rank.Six), _c(Suit.Clubs, Rank.Six),
        ])

        # 预热
        for _ in range(100):
            can_beat(bomb_6, bomb_5)

        start = time.perf_counter()
        for _ in range(100_000):
            can_beat(bomb_6, bomb_5)
        elapsed = time.perf_counter() - start

        assert elapsed < 1.0, f"100,000 次 can_beat 耗时 {elapsed:.2f}s，超过 1s 阈值"


# ================================================================
# 性能测试: 完整对局
# ================================================================

class TestGameSimulationPerformance:

    def _play_one_game(self):
        """用最简 AI 逻辑打一局完整对局。"""
        gm = ServerGameManager()
        gm.start_game()
        first = gm.current_player
        gm.submit_bid(first, 3)

        turns = 0
        while gm.phase != PHASE_GAMEOVER and turns < 500:
            cp = gm.current_player
            hand_cards = [Card.from_dict(cd) for cd in gm.get_hand(cp)]
            if len(hand_cards) == 0:
                break
            sorted_hand = sort_hand(hand_cards)
            smallest = [sorted_hand[-1]]
            combo = classify_play(smallest)
            if gm.last_played_combo is None or not gm.last_played_combo.is_valid:
                gm.submit_play(cp, smallest)
            else:
                if can_beat(combo, gm.last_played_combo):
                    gm.submit_play(cp, smallest)
                else:
                    gm.submit_pass(cp)
            turns += 1
        return turns

    @pytest.mark.performance
    def test_single_ai_game_under_100ms(self):
        # 预热
        self._play_one_game()

        start = time.perf_counter()
        self._play_one_game()
        elapsed = time.perf_counter() - start

        assert elapsed < 0.1, f"单局耗时 {elapsed * 1000:.1f}ms，超过 100ms 阈值"

    @pytest.mark.performance
    def test_100_ai_games_under_10s(self):
        # 预热
        self._play_one_game()

        start = time.perf_counter()
        for _ in range(100):
            self._play_one_game()
        elapsed = time.perf_counter() - start

        assert elapsed < 10.0, f"100 局耗时 {elapsed:.1f}s，超过 10s 阈值"


# ================================================================
# 性能测试: deal_cards
# ================================================================

class TestDealCardsPerformance:

    @pytest.mark.performance
    def test_deal_cards_10000_times_under_5s(self):
        # 预热
        deal_cards()

        start = time.perf_counter()
        for _ in range(10_000):
            deal_cards()
        elapsed = time.perf_counter() - start

        assert elapsed < 5.0, f"10,000 次发牌耗时 {elapsed:.1f}s，超过 5s 阈值"


# ================================================================
# 内存管理测试
# ================================================================

class TestMemoryBehavior:

    @pytest.mark.memory
    def test_classify_play_allocation_pressure(self):
        cards = [_c(Suit.Diamonds, Rank.Three)]

        # 预热 + GC 稳定
        for _ in range(100):
            classify_play(cards)
        gc.collect()

        gen0_before = gc.get_count()[0]

        for _ in range(10_000):
            classify_play(cards)

        gc.collect()
        gen0_after = gc.get_count()[0]
        gen0_delta = gen0_after - gen0_before

        assert gen0_delta < 500, f"Gen0 回收 {gen0_delta} 次，超过 500 上限"

    @pytest.mark.memory
    def test_room_manager_dict_growth_bounded(self):
        rm = RoomManager()

        # 创建 1000 个房间
        for i in range(1000):
            rm.create_room(f"p{i}", f"Player{i}", MockWebSocket())

        assert len(rm._rooms) == 1000

        # 移除所有玩家，房间应自动清理
        for i in range(1000):
            rm.remove_player(f"p{i}")

        assert len(rm._rooms) == 0, "所有房间应已被清理"

        # 再创建 1000 个
        for i in range(1000, 2000):
            rm.create_room(f"p{i}", f"Player{i}", MockWebSocket())

        assert len(rm._rooms) == 1000

    @pytest.mark.memory
    def test_server_game_manager_no_reference_leak(self):
        gc.collect()
        obj_count_before = len(gc.get_objects())

        games = []
        for _ in range(1000):
            gm = ServerGameManager()
            gm.start_game()
            games.append(gm)

        # 释放所有引用
        games.clear()
        gc.collect()

        obj_count_after = len(gc.get_objects())
        growth = obj_count_after - obj_count_before

        # 1000 个游戏管理器创建后释放，对象数增长应有界
        assert growth < 50_000, f"对象数增长 {growth}，超过 50,000 上限"
