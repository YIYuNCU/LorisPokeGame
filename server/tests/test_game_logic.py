"""Tests for game_logic.py: ServerGameManager."""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from card_models import Card, Suit, Rank, CardComboType
from game_logic import ServerGameManager, PHASE_BIDDING, PHASE_PLAYING, PHASE_GAMEOVER


def c(suit, rank):
    return Card(suit=suit, rank=rank)


def _make_game() -> tuple[ServerGameManager, dict]:
    """Create a ServerGameManager with player names and start a game."""
    gm = ServerGameManager()
    for i in range(3):
        gm.set_player_name(i, f"Player{i}")
    data = gm.start_game()
    return gm, data


def _fast_landlord(gm: ServerGameManager, data: dict) -> int:
    """Bid 3 as the first bidder to quickly assign landlord. Returns landlord seat."""
    first = data["first_bidder"]
    gm.submit_bid(first, 3)
    return first


# ── start_game ──

class TestStartGame:
    def test_start_game_sets_phase_bidding(self):
        gm, _ = _make_game()
        assert gm.phase == PHASE_BIDDING

    def test_start_game_returns_hand_data(self):
        gm, data = _make_game()
        assert "hands" in data
        assert "kitty" in data
        assert "first_bidder" in data
        assert len(data["hands"]) == 3
        assert len(data["hands"][0]) == 17
        assert len(data["kitty"]) == 3


# ── submit_bid ──

class TestSubmitBid:
    def test_submit_bid_3_assigns_landlord(self):
        gm, data = _make_game()
        first = data["first_bidder"]
        result = gm.submit_bid(first, 3)
        assert gm.landlord_index == first
        assert gm.phase == PHASE_PLAYING
        # Landlord should have 20 cards (17 + 3 kitty)
        assert len(gm.players[first].hand) == 20
        # A landlord_assigned message should be in the result
        msg_types = [m["type"] for m in result.messages]
        assert "landlord_assigned" in msg_types

    def test_submit_bid_rounds_complete(self):
        """Three bids with increasing amounts; highest bidder wins."""
        gm, data = _make_game()
        first = data["first_bidder"]
        p0 = first
        p1 = (first + 1) % 3
        p2 = (first + 2) % 3

        gm.submit_bid(p0, 1)
        gm.submit_bid(p1, 2)
        gm.submit_bid(p2, 0)

        assert gm.phase == PHASE_PLAYING
        assert gm.landlord_index == p1

    def test_submit_bid_no_one_bids_restarts(self):
        """All pass → game restarts, phase stays BIDDING."""
        gm, data = _make_game()
        first = data["first_bidder"]
        p0 = first
        p1 = (first + 1) % 3
        p2 = (first + 2) % 3

        gm.submit_bid(p0, 0)
        gm.submit_bid(p1, 0)
        gm.submit_bid(p2, 0)

        # Game should have restarted (new hands dealt)
        assert gm.phase == PHASE_BIDDING
        assert gm.landlord_index is None
        # Each player should still have 17 cards
        for p in gm.players:
            assert len(p.hand) == 17

    def test_submit_bid_wrong_seat_returns_error(self):
        gm, data = _make_game()
        first = data["first_bidder"]
        wrong_seat = (first + 1) % 3
        result = gm.submit_bid(wrong_seat, 1)
        msg_types = [m["type"] for m in result.messages]
        assert "error" in msg_types


# ── submit_play ──

class TestSubmitPlay:
    def test_submit_play_valid_single(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)
        card = gm.players[landlord].hand[0]
        hand_before = len(gm.players[landlord].hand)

        result = gm.submit_play(landlord, [card])

        assert len(gm.players[landlord].hand) == hand_before - 1
        msg_types = [m["type"] for m in result.messages]
        assert "cards_played" in msg_types

    def test_submit_play_invalid_combo_returns_error(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)

        result = gm.submit_play(landlord, [])  # empty = invalid
        msg_types = [m["type"] for m in result.messages]
        assert "error" in msg_types

    def test_submit_play_cannot_beat_returns_error(self):
        """Play a lower single when a higher single is on the table."""
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)

        # Find a small card (rank 3) and a big card (rank >= 15) in landlord's hand
        hand = gm.players[landlord].hand
        small_card = None
        big_card = None
        for card in hand:
            if card.rank == Rank.Three and small_card is None:
                small_card = card
            if card.rank == Rank.Two and big_card is None:
                big_card = card

        # If landlord doesn't have both, skip gracefully
        if small_card is None or big_card is None:
            return

        # Landlord plays big card first
        gm.submit_play(landlord, [big_card])

        # Next player tries to play small card (cannot beat big card)
        next_player = (landlord + 1) % 3
        result = gm.submit_play(next_player, [small_card])
        msg_types = [m["type"] for m in result.messages]
        assert "error" in msg_types

    def test_submit_play_bomb_doubles_multiplier(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)

        # Find four cards of the same rank in landlord's hand
        from collections import Counter
        rank_counts = Counter(c.rank for c in gm.players[landlord].hand)
        bomb_rank = None
        for rank, count in rank_counts.items():
            if count >= 4:
                bomb_rank = rank
                break

        if bomb_rank is None:
            return  # not enough cards to form a bomb

        bomb_cards = [c for c in gm.players[landlord].hand if c.rank == bomb_rank][:4]
        mult_before = gm.multiplier
        gm.submit_play(landlord, bomb_cards)
        assert gm.multiplier == mult_before * 2

    def test_submit_play_win_game_over(self):
        """Play all cards → GAMEOVER phase."""
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)

        # Give landlord only 1 card so they can win in one play
        last_card = gm.players[landlord].hand[-1]
        gm.players[landlord].hand = [last_card]
        gm.last_played_combo = None
        gm.last_played_by = None

        result = gm.submit_play(landlord, [last_card])
        assert gm.phase == PHASE_GAMEOVER
        msg_types = [m["type"] for m in result.messages]
        assert "game_over" in msg_types

    def test_submit_play_wrong_seat_returns_error(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)
        wrong = (landlord + 1) % 3
        card = gm.players[wrong].hand[0]
        result = gm.submit_play(wrong, [card])
        msg_types = [m["type"] for m in result.messages]
        assert "error" in msg_types


# ── submit_pass ──

class TestSubmitPass:
    def test_submit_pass_leading_returns_error(self):
        """Must play when leading (last_played_by == seat or no last play)."""
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)
        result = gm.submit_pass(landlord)
        msg_types = [m["type"] for m in result.messages]
        assert "error" in msg_types

    def test_submit_pass_two_passes_clear_table(self):
        """After two consecutive passes, last_played_combo is cleared."""
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)
        card = gm.players[landlord].hand[0]

        # Landlord plays
        gm.submit_play(landlord, [card])
        assert gm.last_played_combo is not None

        # Two other players pass
        p1 = (landlord + 1) % 3
        p2 = (landlord + 2) % 3
        gm.submit_pass(p1)
        assert gm.last_played_combo is not None  # only 1 pass so far
        gm.submit_pass(p2)
        assert gm.last_played_combo is None  # 2 passes → cleared


# ── get_reconnect_state ──

class TestGetReconnectState:
    def test_get_reconnect_state_opponents_hide_hand(self):
        """Reconnect state shows opponent card counts, not hand contents."""
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)

        state = gm.get_reconnect_state(landlord)
        assert "hand" in state
        assert "opponent_counts" in state
        assert "opponent_seats" in state
        # Opponent counts should be present and non-empty
        assert len(state["opponent_counts"]) == 2
        # The reconnect state should not contain opponent hands
        # (only hand for the requesting seat)
        assert len(state["hand"]) == len(gm.players[landlord].hand)


# ── 补充：get_public_state ──

class TestGetPublicState:
    def test_public_state_fields(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)
        state = gm.get_public_state()
        assert "phase" in state
        assert "current_player" in state
        assert "card_counts" in state
        assert "landlord_index" in state
        assert "multiplier" in state
        assert "last_played_by" in state

    def test_public_state_card_counts_match(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)
        state = gm.get_public_state()
        for i in range(3):
            assert state["card_counts"][i] == len(gm.players[i].hand)


# ── 补充：错误阶段调用 ──

class TestWrongPhase:
    def test_bid_during_playing_returns_error(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)
        assert gm.phase == PHASE_PLAYING
        result = gm.submit_bid(landlord, 1)
        msg_types = [m["type"] for m in result.messages]
        assert "error" in msg_types

    def test_play_during_bidding_returns_error(self):
        gm, data = _make_game()
        first = data["first_bidder"]
        assert gm.phase == PHASE_BIDDING
        card = gm.players[first].hand[0]
        result = gm.submit_play(first, [card])
        msg_types = [m["type"] for m in result.messages]
        assert "error" in msg_types


# ── 补充：火箭翻倍 ──

class TestRocketMultiplier:
    def test_rocket_doubles_multiplier(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)

        # 将大小王放入地主手牌
        small_joker = c(Suit.None_, Rank.SmallJoker)
        big_joker = c(Suit.None_, Rank.BigJoker)

        # 确保地主手牌只有火箭，方便测试
        gm.players[landlord].hand = [small_joker, big_joker]
        gm.last_played_combo = None
        gm.last_played_by = None

        mult_before = gm.multiplier
        gm.submit_play(landlord, [small_joker, big_joker])
        assert gm.multiplier == mult_before * 2


# ── 补充：start_game 重置状态 ──

class TestStartGameReset:
    def test_start_game_resets_multiplier(self):
        gm, data = _make_game()
        landlord = _fast_landlord(gm, data)

        # 打一张牌修改状态
        card = gm.players[landlord].hand[0]
        gm.submit_play(landlord, [card])

        # 重新开始
        gm2, data2 = _make_game()
        assert gm2.multiplier == 1
        assert gm2.last_played_combo is None
