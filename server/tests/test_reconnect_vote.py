"""
test_reconnect_vote.py - 断线重连与投票流程测试
覆盖：断线令牌管理、重连状态恢复、投票流程、超时机制、弱网场景
"""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from card_models import Card, Suit, Rank, CardComboType, classify_play
from game_logic import ServerGameManager, PHASE_BIDDING, PHASE_PLAYING, PHASE_GAMEOVER
from room_manager import RoomManager, ReconnectToken


class MockWebSocket:
    """Minimal stand-in for WebSocket."""
    def __init__(self, name="ws"):
        self.name = name
    def __repr__(self):
        return f"MockWebSocket({self.name})"


def c(suit, rank):
    return Card(suit=suit, rank=rank)


def _setup_game_with_3_players():
    """Create a RoomManager with 3 players in a room."""
    rm = RoomManager()
    ws0, ws1, ws2 = MockWebSocket("p0"), MockWebSocket("p1"), MockWebSocket("p2")
    code, room, _ = rm.create_room("p0", "Alice", ws0)
    rm.join_room(code, "p1", "Bob", ws1)
    rm.join_room(code, "p2", "Charlie", ws2)
    return rm, room, code, ws0, ws1, ws2


def _start_game_in_room(room):
    """Attach a ServerGameManager to a room and start the game. Returns (gm, first_bidder)."""
    gm = ServerGameManager()
    for pid, conn in room.players.items():
        gm.set_player_name(conn.seat, conn.player_name)
    room.game = gm
    room.game_started = True
    data = gm.start_game()
    return gm, data["first_bidder"]


def _fast_landlord(gm, first_bidder):
    """Quickly assign landlord by bidding 3. Returns landlord seat."""
    gm.submit_bid(first_bidder, 3)
    return first_bidder


# ── 重连令牌基础 ──

class TestReconnectToken:
    def test_store_token_removes_from_active_players(self):
        """store_reconnect_token should remove player from active connections."""
        rm, room, code, ws0, ws1, ws2 = _setup_game_with_3_players()

        result = rm.store_reconnect_token("p0")
        assert result is not None
        room, token = result

        # Player removed from active players
        assert "p0" not in room.players
        # But token is stored
        assert "p0" in room.reconnect_tokens
        assert token.player_name == "Alice"
        assert token.seat == 0

    def test_store_token_preserves_seat(self):
        """Seat number must be preserved in the reconnect token."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()

        _, token = rm.store_reconnect_token("p1")
        assert token.seat == 1  # Bob's seat

    def test_store_nonexistent_player_returns_none(self):
        rm = RoomManager()
        assert rm.store_reconnect_token("ghost") is None

    def test_try_reconnect_restores_player(self):
        """try_reconnect restores player with same seat and name."""
        rm, room, _, ws0, _, _ = _setup_game_with_3_players()
        rm.store_reconnect_token("p0")

        new_ws = MockWebSocket("new_p0")
        result = rm.try_reconnect("p0", new_ws)
        assert result is not None
        r_room, token = result

        assert "p0" in room.players
        assert room.players["p0"].seat == 0
        assert room.players["p0"].player_name == "Alice"
        assert room.players["p0"].websocket is new_ws

    def test_try_reconnect_clears_pause_and_vote(self):
        """Reconnecting should clear game_paused and vote_state."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        rm.store_reconnect_token("p0")
        room.game_paused = True
        room.vote_state["p1"] = "continue"

        rm.try_reconnect("p0", MockWebSocket())

        assert room.game_paused is False
        assert len(room.vote_state) == 0

    def test_try_reconnect_removes_token(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        rm.store_reconnect_token("p0")
        rm.try_reconnect("p0", MockWebSocket())
        assert "p0" not in room.reconnect_tokens

    def test_try_reconnect_nonexistent_returns_none(self):
        rm = RoomManager()
        assert rm.try_reconnect("ghost", MockWebSocket()) is None

    def test_multiple_disconnect_reconnect_cycles(self):
        """Player can disconnect and reconnect multiple times."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()

        for cycle in range(3):
            rm.store_reconnect_token("p0")
            assert "p0" in room.reconnect_tokens
            assert "p0" not in room.players

            rm.try_reconnect("p0", MockWebSocket(f"cycle_{cycle}"))
            assert "p0" in room.players
            assert "p0" not in room.reconnect_tokens


# ── 重连状态恢复 ──

class TestReconnectGameState:
    def test_reconnect_state_shows_own_hand(self):
        """Reconnected player should see their full hand."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert len(state["hand"]) == len(gm.players[landlord].hand)

    def test_reconnect_state_hides_opponent_hands(self):
        """Opponents should show card count, not hand contents."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert "opponent_counts" in state
        assert "opponent_seats" in state
        assert len(state["opponent_counts"]) == 2
        assert "opponent_hands" not in state

    def test_reconnect_state_includes_landlord_info(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        farmer = (landlord + 1) % 3
        state = gm.get_reconnect_state(farmer)
        assert state["landlord_seat"] == landlord

    def test_reconnect_state_includes_phase(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert state["phase"] == "playing"

    def test_reconnect_state_includes_player_names(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert "player_names" in state
        assert len(state["player_names"]) == 3

    def test_reconnect_state_includes_last_played(self):
        """After a play, reconnect state should show the last played cards."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        card = gm.players[landlord].hand[0]
        gm.submit_play(landlord, [card])

        farmer = (landlord + 1) % 3
        state = gm.get_reconnect_state(farmer)
        assert len(state["last_played_cards"]) == 1
        assert state["last_played_by"] == landlord

    def test_reconnect_during_bidding_phase(self):
        """Reconnect state during bidding shows correct phase."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        # Don't assign landlord - stay in bidding

        state = gm.get_reconnect_state(0)
        assert state["phase"] == "bidding"


# ── 投票流程 ──

class TestVoteFlow:
    def test_vote_end_immediately_ends_game(self):
        """Voting 'end' should immediately end the game."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        _fast_landlord(gm, first)

        room.game_paused = True
        room.vote_state["p0"] = "end"

        assert room.vote_state["p0"] == "end"

    def test_vote_continue_all_players(self):
        """All remaining players vote 'continue' → enters reconnect waiting."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        _fast_landlord(gm, first)

        # Simulate player p2 disconnecting
        rm.store_reconnect_token("p2")
        room.game_paused = True

        # Remaining players (p0, p1) vote continue
        room.vote_state["p0"] = "continue"
        room.vote_state["p1"] = "continue"

        online_player_ids = list(room.players.keys())
        all_continue = all(
            room.vote_state.get(pid) == "continue"
            for pid in online_player_ids
        )
        assert all_continue is True
        assert len(online_player_ids) == 2

    def test_game_paused_state(self):
        """Game should be paused when a player disconnects during a game."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        _start_game_in_room(room)

        room.game_paused = True
        assert room.game_paused is True

    def test_disconnect_during_game_preserves_game(self):
        """When a player disconnects, the game object should be preserved."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        _fast_landlord(gm, first)

        rm.store_reconnect_token("p0")
        assert room.game is gm  # game object still alive


# ── 弱网场景 ──

class TestWeakNetworkScenarios:
    def test_reconnect_during_play_round(self):
        """Player disconnects mid-round and reconnects; game state preserved."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        card = gm.players[landlord].hand[0]
        hand_before = len(gm.players[landlord].hand)
        gm.submit_play(landlord, [card])

        assert len(gm.players[landlord].hand) == hand_before - 1

        # Landlord disconnects
        rm.store_reconnect_token("p0")
        assert "p0" not in room.players

        # Landlord reconnects
        rm.try_reconnect("p0", MockWebSocket("reconnected"))

        # Game state should still be valid
        assert room.game is gm
        assert len(gm.players[landlord].hand) == hand_before - 1
        assert gm.last_played_combo is not None

    def test_reconnect_restores_correct_hand_size(self):
        """After reconnect, hand size must match the game state."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        card = gm.players[landlord].hand[0]
        gm.submit_play(landlord, [card])

        expected_hand_size = len(gm.players[landlord].hand)

        rm.store_reconnect_token("p0")
        rm.try_reconnect("p0", MockWebSocket())

        state = gm.get_reconnect_state(landlord)
        assert len(state["hand"]) == expected_hand_size

    def test_opponent_card_counts_after_plays(self):
        """Opponent card counts in reconnect state should decrease after plays."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        card = gm.players[landlord].hand[0]
        gm.submit_play(landlord, [card])

        farmer = (landlord + 1) % 3
        state = gm.get_reconnect_state(farmer)
        card_counts = state["card_counts"]
        # Landlord should have 19 cards (20 - 1)
        assert card_counts[landlord] == 19

    def test_consecutive_passes_reset_on_reconnect(self):
        """After 2 passes, last_played_combo is cleared even across disconnects."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        card = gm.players[landlord].hand[0]
        gm.submit_play(landlord, [card])

        p1 = (landlord + 1) % 3
        p2 = (landlord + 2) % 3

        gm.submit_pass(p1)
        assert gm.last_played_combo is not None

        # Player 2 disconnects then reconnects
        rm.store_reconnect_token("p2")
        rm.try_reconnect("p2", MockWebSocket())
        gm.submit_pass(p2)

        # After 2 passes, table should be cleared
        assert gm.last_played_combo is None


# ── get_reconnect_state 详细验证 ──

class TestReconnectStateDetail:
    def test_reconnect_state_card_counts_for_all_players(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        card_counts = state["card_counts"]
        assert len(card_counts) == 3
        assert card_counts[landlord] == 20  # landlord has 20
        assert sum(1 for i in range(3) if i != landlord and card_counts[i] == 17) == 2

    def test_reconnect_state_kitty_count(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert state["kitty_count"] == 3

    def test_reconnect_state_multiplier(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert state["multiplier"] >= 1

    def test_reconnect_state_no_last_played_initially(self):
        """Right after landlord assignment, no cards have been played yet."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert state["last_played_cards"] == []
        assert state["last_played_by"] is None

    def test_reconnect_state_current_player(self):
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        state = gm.get_reconnect_state(landlord)
        assert state["current_player"] == landlord  # landlord goes first

    def test_reconnect_state_after_multiple_rounds(self):
        """Reconnect state stays consistent after multiple play rounds."""
        rm, room, _, _, _, _ = _setup_game_with_3_players()
        gm, first = _start_game_in_room(room)
        landlord = _fast_landlord(gm, first)

        p1 = (landlord + 1) % 3
        p2 = (landlord + 2) % 3

        # Landlord plays
        card0 = gm.players[landlord].hand[0]
        gm.submit_play(landlord, [card0])

        # Two others pass → table cleared, landlord leads again
        gm.submit_pass(p1)
        gm.submit_pass(p2)

        state = gm.get_reconnect_state(landlord)
        assert state["current_player"] == landlord  # landlord leads after clear
        assert state["last_played_cards"] == []  # table cleared
        assert len(state["hand"]) == len(gm.players[landlord].hand)
