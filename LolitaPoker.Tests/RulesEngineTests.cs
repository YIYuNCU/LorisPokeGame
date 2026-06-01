using System.Collections.Generic;
using System.Linq;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Tests;

public class RulesEngineTests
{
    // ========== ClassifyPlay tests ==========

    [Fact]
    public void ClassifyPlay_Single()
    {
        var cards = new List<Card> { new Card(Suit.Diamonds, Rank.Three) };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.Single, combo.Type);
        Assert.Equal(Rank.Three, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_Pair()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Seven),
            new Card(Suit.Hearts, Rank.Seven),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.Pair, combo.Type);
        Assert.Equal(Rank.Seven, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_Triple()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Ten),
            new Card(Suit.Hearts, Rank.Ten),
            new Card(Suit.Spades, Rank.Ten),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.Triple, combo.Type);
        Assert.Equal(Rank.Ten, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_Rocket()
    {
        var cards = new List<Card>
        {
            new Card(Suit.None, Rank.SmallJoker),
            new Card(Suit.None, Rank.BigJoker),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.Rocket, combo.Type);
        Assert.Equal(Rank.BigJoker, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_Bomb()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Eight),
            new Card(Suit.Hearts, Rank.Eight),
            new Card(Suit.Spades, Rank.Eight),
            new Card(Suit.Clubs, Rank.Eight),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.Bomb, combo.Type);
        Assert.Equal(Rank.Eight, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_TriplePlusOne()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Diamonds, Rank.Three),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.TriplePlusOne, combo.Type);
        Assert.Equal(Rank.Five, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_TriplePlusPair()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Diamonds, Rank.Nine),
            new Card(Suit.Hearts, Rank.Nine),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.TriplePlusPair, combo.Type);
        Assert.Equal(Rank.Five, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_FourPlusTwo()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Jack),
            new Card(Suit.Hearts, Rank.Jack),
            new Card(Suit.Spades, Rank.Jack),
            new Card(Suit.Clubs, Rank.Jack),
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.FourPlusTwo, combo.Type);
        Assert.Equal(Rank.Jack, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_FourPlusTwoPairs()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Jack),
            new Card(Suit.Hearts, Rank.Jack),
            new Card(Suit.Spades, Rank.Jack),
            new Card(Suit.Clubs, Rank.Jack),
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
            new Card(Suit.Hearts, Rank.Four),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.FourPlusTwoPairs, combo.Type);
        Assert.Equal(Rank.Jack, combo.PrimaryRank);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_Straight()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.Straight, combo.Type);
        Assert.Equal(Rank.Seven, combo.PrimaryRank);
        Assert.Equal(5, combo.ChainLength);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_StraightWithTwoIsInvalid()
    {
        // 2 cannot be part of a straight
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Ten),
            new Card(Suit.Diamonds, Rank.Jack),
            new Card(Suit.Diamonds, Rank.Queen),
            new Card(Suit.Diamonds, Rank.King),
            new Card(Suit.Diamonds, Rank.Two),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.False(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_ConsecutivePairs()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.ConsecutivePairs, combo.Type);
        Assert.Equal(Rank.Five, combo.PrimaryRank);
        Assert.Equal(3, combo.ChainLength);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_Airplane()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Four),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.Airplane, combo.Type);
        Assert.Equal(Rank.Four, combo.PrimaryRank);
        Assert.Equal(2, combo.ChainLength);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_AirplaneWithSingles()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Four),
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.AirplaneWithSingles, combo.Type);
        Assert.Equal(Rank.Four, combo.PrimaryRank);
        Assert.Equal(2, combo.ChainLength);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_AirplaneWithPairs()
    {
        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Four),
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven),
            new Card(Suit.Hearts, Rank.Seven),
        };
        var combo = RulesEngine.ClassifyPlay(cards);

        Assert.Equal(CardComboType.AirplaneWithPairs, combo.Type);
        Assert.Equal(Rank.Four, combo.PrimaryRank);
        Assert.Equal(2, combo.ChainLength);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void ClassifyPlay_EmptyInput_ReturnsInvalid()
    {
        var combo = RulesEngine.ClassifyPlay(new List<Card>());
        Assert.False(combo.IsValid);
        Assert.Equal(CardComboType.None, combo.Type);
    }

    // ========== CanBeat tests ==========

    [Fact]
    public void CanBeat_RocketBeatsEverything()
    {
        var rocket = new CardCombo(CardComboType.Rocket, Rank.BigJoker, 1,
            new List<Card>
            {
                new Card(Suit.None, Rank.SmallJoker),
                new Card(Suit.None, Rank.BigJoker),
            });

        var single = new CardCombo(CardComboType.Single, Rank.Two, 1,
            new List<Card> { new Card(Suit.Diamonds, Rank.Two) });

        Assert.True(RulesEngine.CanBeat(rocket, single));
    }

    [Fact]
    public void CanBeat_BombBeatsNonBomb()
    {
        var bomb = new CardCombo(CardComboType.Bomb, Rank.Three, 1,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Three),
                new Card(Suit.Hearts, Rank.Three),
                new Card(Suit.Spades, Rank.Three),
                new Card(Suit.Clubs, Rank.Three),
            });

        var pair = new CardCombo(CardComboType.Pair, Rank.Ace, 1,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Ace),
                new Card(Suit.Hearts, Rank.Ace),
            });

        Assert.True(RulesEngine.CanBeat(bomb, pair));
    }

    [Fact]
    public void CanBeat_HigherBombBeatsLowerBomb()
    {
        var lowBomb = new CardCombo(CardComboType.Bomb, Rank.Three, 1,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Three),
                new Card(Suit.Hearts, Rank.Three),
                new Card(Suit.Spades, Rank.Three),
                new Card(Suit.Clubs, Rank.Three),
            });

        var highBomb = new CardCombo(CardComboType.Bomb, Rank.Eight, 1,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Eight),
                new Card(Suit.Hearts, Rank.Eight),
                new Card(Suit.Spades, Rank.Eight),
                new Card(Suit.Clubs, Rank.Eight),
            });

        Assert.True(RulesEngine.CanBeat(highBomb, lowBomb));
        Assert.False(RulesEngine.CanBeat(lowBomb, highBomb));
    }

    [Fact]
    public void CanBeat_RocketBeatsBomb()
    {
        var rocket = new CardCombo(CardComboType.Rocket, Rank.BigJoker, 1,
            new List<Card>
            {
                new Card(Suit.None, Rank.SmallJoker),
                new Card(Suit.None, Rank.BigJoker),
            });

        var bomb = new CardCombo(CardComboType.Bomb, Rank.Two, 1,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Two),
                new Card(Suit.Hearts, Rank.Two),
                new Card(Suit.Spades, Rank.Two),
                new Card(Suit.Clubs, Rank.Two),
            });

        Assert.True(RulesEngine.CanBeat(rocket, bomb));
        Assert.False(RulesEngine.CanBeat(bomb, rocket));
    }

    [Fact]
    public void CanBeat_SameTypeHigherRankWins()
    {
        var low = new CardCombo(CardComboType.Single, Rank.Five, 1,
            new List<Card> { new Card(Suit.Diamonds, Rank.Five) });

        var high = new CardCombo(CardComboType.Single, Rank.Ace, 1,
            new List<Card> { new Card(Suit.Diamonds, Rank.Ace) });

        Assert.True(RulesEngine.CanBeat(high, low));
        Assert.False(RulesEngine.CanBeat(low, high));
    }

    [Fact]
    public void CanBeat_DifferentChainLengthCannotBeat()
    {
        // 5-card straight
        var straight5 = new CardCombo(CardComboType.Straight, Rank.Seven, 5,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Three),
                new Card(Suit.Diamonds, Rank.Four),
                new Card(Suit.Diamonds, Rank.Five),
                new Card(Suit.Diamonds, Rank.Six),
                new Card(Suit.Diamonds, Rank.Seven),
            });

        // 6-card straight (higher primary rank but different chain length)
        var straight6 = new CardCombo(CardComboType.Straight, Rank.Eight, 6,
            new List<Card>
            {
                new Card(Suit.Diamonds, Rank.Three),
                new Card(Suit.Diamonds, Rank.Four),
                new Card(Suit.Diamonds, Rank.Five),
                new Card(Suit.Diamonds, Rank.Six),
                new Card(Suit.Diamonds, Rank.Seven),
                new Card(Suit.Diamonds, Rank.Eight),
            });

        Assert.False(RulesEngine.CanBeat(straight6, straight5));
    }

    [Fact]
    public void CanBeat_InvalidCannotBeat()
    {
        var invalid = CardCombo.Invalid;

        var single = new CardCombo(CardComboType.Single, Rank.Three, 1,
            new List<Card> { new Card(Suit.Diamonds, Rank.Three) });

        Assert.False(RulesEngine.CanBeat(invalid, single));
        Assert.False(RulesEngine.CanBeat(single, invalid));
    }
}
