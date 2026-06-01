"""Tests for models.py: Pydantic message models."""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from models import ClientMessage, CardData, CreateRoomPayload, BidPayload, PlayPayload, JoinRoomPayload, SetRoomVisibilityPayload
from card_models import Card, Suit, Rank


class TestClientMessage:
    def test_client_message_default_payload(self):
        msg = ClientMessage(type="test")
        assert msg.type == "test"
        assert msg.payload == {}


class TestCreateRoomPayload:
    def test_create_room_defaults(self):
        payload = CreateRoomPayload()
        assert payload.player_name == "玩家"
        assert payload.is_public is True


class TestBidPayload:
    def test_bid_payload_range(self):
        assert BidPayload(amount=0).amount == 0
        assert BidPayload(amount=3).amount == 3


class TestPlayPayload:
    def test_play_payload_cards(self):
        payload = PlayPayload(cards=[CardData(suit=0, rank=3)])
        assert len(payload.cards) == 1
        assert payload.cards[0].suit == 0
        assert payload.cards[0].rank == 3


class TestCardData:
    def test_card_data_from_card(self):
        """Card.to_dict() should produce a valid CardData."""
        card = Card(suit=Suit.Spades, rank=Rank.Ace)
        d = card.to_dict()
        cd = CardData(**d)
        assert cd.suit == Suit.Spades
        assert cd.rank == Rank.Ace

    def test_card_data_roundtrip(self):
        card = Card(suit=Suit.Hearts, rank=Rank.Ten)
        cd = CardData(**card.to_dict())
        assert Card.from_dict(cd.model_dump()) == card


# ── 补充：JoinRoomPayload ──

class TestJoinRoomPayload:
    def test_defaults(self):
        payload = JoinRoomPayload(room_code="ABC123")
        assert payload.room_code == "ABC123"
        assert payload.player_name == "玩家"

    def test_custom_name(self):
        payload = JoinRoomPayload(room_code="ABC123", player_name="Bob")
        assert payload.player_name == "Bob"


# ── 补充：SetRoomVisibilityPayload ──

class TestSetRoomVisibilityPayload:
    def test_public(self):
        payload = SetRoomVisibilityPayload(is_public=True)
        assert payload.is_public is True

    def test_private(self):
        payload = SetRoomVisibilityPayload(is_public=False)
        assert payload.is_public is False
