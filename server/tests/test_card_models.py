"""Tests for card_models.py: Card, classify_play, can_beat, deal_cards."""

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from card_models import (
    Card, Suit, Rank, CardComboType,
    create_full_deck, classify_play, can_beat, deal_cards,
)


def c(suit, rank):
    return Card(suit=suit, rank=rank)


# ── create_full_deck ──

class TestCreateFullDeck:
    def test_full_deck_has_54_cards(self, full_deck):
        assert len(full_deck) == 54

    def test_full_deck_no_duplicates(self, full_deck):
        seen = set()
        for card in full_deck:
            key = (card.suit, card.rank)
            assert key not in seen
            seen.add(key)
        assert len(seen) == 54


# ── classify_play ──

class TestClassifyPlay:
    def test_single(self):
        combo = classify_play([c(Suit.Diamonds, Rank.Three)])
        assert combo.type == CardComboType.Single
        assert combo.primary_rank == Rank.Three

    def test_pair(self):
        combo = classify_play([c(Suit.Diamonds, Rank.Seven), c(Suit.Spades, Rank.Seven)])
        assert combo.type == CardComboType.Pair
        assert combo.primary_rank == Rank.Seven

    def test_triple(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Five),
            c(Suit.Spades, Rank.Five),
            c(Suit.Hearts, Rank.Five),
        ])
        assert combo.type == CardComboType.Triple
        assert combo.primary_rank == Rank.Five

    def test_rocket(self):
        combo = classify_play([
            c(Suit.None_, Rank.SmallJoker),
            c(Suit.None_, Rank.BigJoker),
        ])
        assert combo.type == CardComboType.Rocket
        assert combo.primary_rank == Rank.BigJoker

    def test_bomb(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Nine),
            c(Suit.Spades, Rank.Nine),
            c(Suit.Hearts, Rank.Nine),
            c(Suit.Clubs, Rank.Nine),
        ])
        assert combo.type == CardComboType.Bomb
        assert combo.primary_rank == Rank.Nine

    def test_triple_plus_one(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Four),
            c(Suit.Spades, Rank.Four),
            c(Suit.Hearts, Rank.Four),
            c(Suit.Clubs, Rank.Three),
        ])
        assert combo.type == CardComboType.TriplePlusOne
        assert combo.primary_rank == Rank.Four

    def test_triple_plus_pair(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Six),
            c(Suit.Spades, Rank.Six),
            c(Suit.Hearts, Rank.Six),
            c(Suit.Clubs, Rank.Eight),
            c(Suit.Diamonds, Rank.Eight),
        ])
        assert combo.type == CardComboType.TriplePlusPair
        assert combo.primary_rank == Rank.Six

    def test_four_plus_two(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Ten),
            c(Suit.Spades, Rank.Ten),
            c(Suit.Hearts, Rank.Ten),
            c(Suit.Clubs, Rank.Ten),
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Four),
        ])
        assert combo.type == CardComboType.FourPlusTwo
        assert combo.primary_rank == Rank.Ten

    def test_four_plus_two_pairs(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Jack),
            c(Suit.Spades, Rank.Jack),
            c(Suit.Hearts, Rank.Jack),
            c(Suit.Clubs, Rank.Jack),
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Three),
            c(Suit.Hearts, Rank.Five),
            c(Suit.Clubs, Rank.Five),
        ])
        assert combo.type == CardComboType.FourPlusTwoPairs
        assert combo.primary_rank == Rank.Jack

    def test_straight(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Four),
            c(Suit.Hearts, Rank.Five),
            c(Suit.Clubs, Rank.Six),
            c(Suit.Diamonds, Rank.Seven),
        ])
        assert combo.type == CardComboType.Straight
        assert combo.primary_rank == Rank.Seven
        assert combo.chain_length == 5

    def test_consecutive_pairs(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Three),
            c(Suit.Diamonds, Rank.Four),
            c(Suit.Spades, Rank.Four),
            c(Suit.Diamonds, Rank.Five),
            c(Suit.Spades, Rank.Five),
        ])
        assert combo.type == CardComboType.ConsecutivePairs
        assert combo.primary_rank == Rank.Five
        assert combo.chain_length == 3

    def test_airplane(self):
        combo = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Three),
            c(Suit.Hearts, Rank.Three),
            c(Suit.Diamonds, Rank.Four),
            c(Suit.Spades, Rank.Four),
            c(Suit.Hearts, Rank.Four),
        ])
        assert combo.type == CardComboType.Airplane
        assert combo.primary_rank == Rank.Four
        assert combo.chain_length == 2

    def test_empty_input_returns_invalid(self):
        combo = classify_play([])
        assert not combo.is_valid
        assert combo.type == CardComboType.None_

    def test_invalid_mixed_cards_returns_invalid(self):
        # Two different singles that form no valid combo
        combo = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Five),
        ])
        assert not combo.is_valid
        assert combo.type == CardComboType.None_


# ── can_beat ──

class TestCanBeat:
    def test_rocket_beats_everything(self):
        bomb = classify_play([
            c(Suit.Diamonds, Rank.Ace),
            c(Suit.Spades, Rank.Ace),
            c(Suit.Hearts, Rank.Ace),
            c(Suit.Clubs, Rank.Ace),
        ])
        rocket = classify_play([c(Suit.None_, Rank.SmallJoker), c(Suit.None_, Rank.BigJoker)])
        assert can_beat(rocket, bomb) is True

    def test_bomb_beats_non_bomb(self):
        triple = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Three),
            c(Suit.Hearts, Rank.Three),
        ])
        bomb = classify_play([
            c(Suit.Diamonds, Rank.Four),
            c(Suit.Spades, Rank.Four),
            c(Suit.Hearts, Rank.Four),
            c(Suit.Clubs, Rank.Four),
        ])
        assert can_beat(bomb, triple) is True

    def test_higher_bomb_beats_lower_bomb(self):
        low_bomb = classify_play([
            c(Suit.Diamonds, Rank.Five),
            c(Suit.Spades, Rank.Five),
            c(Suit.Hearts, Rank.Five),
            c(Suit.Clubs, Rank.Five),
        ])
        high_bomb = classify_play([
            c(Suit.Diamonds, Rank.Ten),
            c(Suit.Spades, Rank.Ten),
            c(Suit.Hearts, Rank.Ten),
            c(Suit.Clubs, Rank.Ten),
        ])
        assert can_beat(high_bomb, low_bomb) is True

    def test_same_type_higher_wins(self):
        low = classify_play([c(Suit.Diamonds, Rank.Three)])
        high = classify_play([c(Suit.Diamonds, Rank.Eight)])
        assert can_beat(high, low) is True

    def test_different_type_cannot_beat(self):
        pair = classify_play([c(Suit.Diamonds, Rank.Four), c(Suit.Spades, Rank.Four)])
        single = classify_play([c(Suit.Diamonds, Rank.Three)])
        assert can_beat(pair, single) is False

    def test_same_type_lower_cannot_beat(self):
        high = classify_play([c(Suit.Diamonds, Rank.Ten)])
        low = classify_play([c(Suit.Diamonds, Rank.Four)])
        assert can_beat(low, high) is False

    def test_rocket_cannot_be_beaten_by_bomb(self):
        rocket = classify_play([c(Suit.None_, Rank.SmallJoker), c(Suit.None_, Rank.BigJoker)])
        bomb = classify_play([
            c(Suit.Diamonds, Rank.Ace),
            c(Suit.Spades, Rank.Ace),
            c(Suit.Hearts, Rank.Ace),
            c(Suit.Clubs, Rank.Ace),
        ])
        assert can_beat(bomb, rocket) is False


# ── deal_cards ──

class TestDealCards:
    def test_deal_returns_correct_counts(self):
        h0, h1, h2, kitty = deal_cards()
        assert len(h0) == 17
        assert len(h1) == 17
        assert len(h2) == 17
        assert len(kitty) == 3
        total = h0 + h1 + h2 + kitty
        assert len(total) == 54

    def test_deal_no_duplicates(self):
        h0, h1, h2, kitty = deal_cards()
        all_cards = h0 + h1 + h2 + kitty
        keys = [(c.suit, c.rank) for c in all_cards]
        assert len(keys) == len(set(keys))


# ── 补充：Card 属性和序列化 ──

class TestCard:
    def test_strength_equals_rank(self):
        card = c(Suit.Diamonds, Rank.Ace)
        assert card.strength == Rank.Ace

    def test_strength_big_joker(self):
        card = c(Suit.None_, Rank.BigJoker)
        assert card.strength == Rank.BigJoker

    def test_is_joker_small(self):
        card = c(Suit.None_, Rank.SmallJoker)
        assert card.is_joker is True

    def test_is_joker_big(self):
        card = c(Suit.None_, Rank.BigJoker)
        assert card.is_joker is True

    def test_is_joker_regular(self):
        card = c(Suit.Diamonds, Rank.Ace)
        assert card.is_joker is False

    def test_to_dict_roundtrip(self):
        card = c(Suit.Spades, Rank.King)
        d = card.to_dict()
        restored = Card.from_dict(d)
        assert restored == card

    def test_to_dict_roundtrip_joker(self):
        card = c(Suit.None_, Rank.BigJoker)
        d = card.to_dict()
        restored = Card.from_dict(d)
        assert restored == card


# ── 补充：classify_play 边缘场景 ──

class TestClassifyPlayExtended:
    def test_airplane_with_singles(self):
        # 2个连续三条 + 2个单张 = 飞机带单
        combo = classify_play([
            c(Suit.Diamonds, Rank.Five),
            c(Suit.Spades, Rank.Five),
            c(Suit.Hearts, Rank.Five),
            c(Suit.Diamonds, Rank.Six),
            c(Suit.Spades, Rank.Six),
            c(Suit.Hearts, Rank.Six),
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Four),
        ])
        assert combo.type == CardComboType.AirplaneWithSingles
        assert combo.primary_rank == Rank.Six
        assert combo.chain_length == 2

    def test_airplane_with_pairs(self):
        # 2个连续三条 + 2个对子 = 飞机带对
        combo = classify_play([
            c(Suit.Diamonds, Rank.Five),
            c(Suit.Spades, Rank.Five),
            c(Suit.Hearts, Rank.Five),
            c(Suit.Diamonds, Rank.Six),
            c(Suit.Spades, Rank.Six),
            c(Suit.Hearts, Rank.Six),
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Three),
            c(Suit.Diamonds, Rank.Four),
            c(Suit.Spades, Rank.Four),
        ])
        assert combo.type == CardComboType.AirplaneWithPairs
        assert combo.primary_rank == Rank.Six
        assert combo.chain_length == 2

    def test_straight_with_joker_rejected(self):
        # 含王的顺子应判无效
        combo = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Four),
            c(Suit.Spades, Rank.Five),
            c(Suit.Clubs, Rank.Six),
            c(Suit.None_, Rank.SmallJoker),
        ])
        assert not combo.is_valid

    def test_straight_with_two_rejected(self):
        # 含2的顺子应判无效
        combo = classify_play([
            c(Suit.Diamonds, Rank.Ten),
            c(Suit.Spades, Rank.Jack),
            c(Suit.Spades, Rank.Queen),
            c(Suit.Clubs, Rank.King),
            c(Suit.Diamonds, Rank.Two),
        ])
        assert not combo.is_valid


# ── 补充：can_beat 边缘场景 ──

class TestCanBeatExtended:
    def test_candidate_invalid_returns_false(self):
        from card_models import CardCombo
        valid = classify_play([c(Suit.Diamonds, Rank.Three)])
        invalid = CardCombo.invalid()
        assert can_beat(invalid, valid) is False

    def test_same_type_different_chain_length(self):
        # 5张顺子 vs 6张顺子，不能互相压
        s5 = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Four),
            c(Suit.Spades, Rank.Five),
            c(Suit.Clubs, Rank.Six),
            c(Suit.Diamonds, Rank.Seven),
        ])
        s6 = classify_play([
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.Four),
            c(Suit.Spades, Rank.Five),
            c(Suit.Clubs, Rank.Six),
            c(Suit.Diamonds, Rank.Seven),
            c(Suit.Diamonds, Rank.Eight),
        ])
        assert can_beat(s6, s5) is False


# ── 补充：sort_hand ──

class TestSortHand:
    def test_sort_hand_returns_descending(self):
        from card_models import sort_hand
        hand = [
            c(Suit.Diamonds, Rank.Three),
            c(Suit.Spades, Rank.BigJoker),
            c(Suit.Hearts, Rank.Ace),
            c(Suit.Clubs, Rank.Seven),
            c(Suit.Diamonds, Rank.Two),
        ]
        sorted_hand = sort_hand(hand)
        for i in range(len(sorted_hand) - 1):
            assert sorted_hand[i].strength >= sorted_hand[i + 1].strength
