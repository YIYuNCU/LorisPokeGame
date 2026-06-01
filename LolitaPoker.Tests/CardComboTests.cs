// -----------------------------------------------------------------------
// CardComboTests.cs - 牌型组合测试
// -----------------------------------------------------------------------

using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.Game;

namespace LolitaPoker.Tests;

public class CardComboTests
{
    private static CardCombo MakeCombo(CardComboType type, Rank primary, int chainLen = 1, int cardCount = 1)
    {
        var cards = Enumerable.Range(0, cardCount)
            .Select(_ => new Card(Suit.Diamonds, Rank.Three))
            .ToArray();
        return new CardCombo(type, primary, chainLen, cards);
    }

    [Fact]
    public void Invalid_Singleton_TypeIsNone()
    {
        Assert.Equal(CardComboType.None, CardCombo.Invalid.Type);
    }

    [Fact]
    public void Invalid_Singleton_IsValidReturnsFalse()
    {
        Assert.False(CardCombo.Invalid.IsValid);
    }

    [Fact]
    public void IsValid_ValidCombo_ReturnsTrue()
    {
        var combo = MakeCombo(CardComboType.Single, Rank.Three);
        Assert.True(combo.IsValid);
    }

    [Fact]
    public void GetDescription_Single_Returns单张()
    {
        var combo = MakeCombo(CardComboType.Single, Rank.Three);
        Assert.Equal("单张", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_Pair_Returns对子()
    {
        var combo = MakeCombo(CardComboType.Pair, Rank.Five);
        Assert.Equal("对子", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_Triple_Returns三条()
    {
        var combo = MakeCombo(CardComboType.Triple, Rank.Seven);
        Assert.Equal("三条", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_TriplePlusOne_Returns三带一()
    {
        var combo = MakeCombo(CardComboType.TriplePlusOne, Rank.Eight);
        Assert.Equal("三带一", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_TriplePlusPair_Returns三带二()
    {
        var combo = MakeCombo(CardComboType.TriplePlusPair, Rank.Nine);
        Assert.Equal("三带二", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_Straight_IncludesChainLength()
    {
        var combo = MakeCombo(CardComboType.Straight, Rank.Three, chainLen: 5);
        Assert.Equal("顺子(5)", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_ConsecutivePairs_IncludesChainLength()
    {
        var combo = MakeCombo(CardComboType.ConsecutivePairs, Rank.Four, chainLen: 3);
        Assert.Equal("连对(3)", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_Airplane_IncludesChainLength()
    {
        var combo = MakeCombo(CardComboType.Airplane, Rank.Five, chainLen: 2);
        Assert.Equal("飞机(2)", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_AirplaneWithSingles_IncludesChainLength()
    {
        var combo = MakeCombo(CardComboType.AirplaneWithSingles, Rank.Five, chainLen: 2);
        Assert.Equal("飞机带单(2)", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_AirplaneWithPairs_IncludesChainLength()
    {
        var combo = MakeCombo(CardComboType.AirplaneWithPairs, Rank.Five, chainLen: 2);
        Assert.Equal("飞机带对(2)", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_FourPlusTwo_Returns四带二()
    {
        var combo = MakeCombo(CardComboType.FourPlusTwo, Rank.Ten);
        Assert.Equal("四带二", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_FourPlusTwoPairs_Returns四带二对()
    {
        var combo = MakeCombo(CardComboType.FourPlusTwoPairs, Rank.Jack);
        Assert.Equal("四带二对", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_Bomb_Contains炸弹()
    {
        var combo = MakeCombo(CardComboType.Bomb, Rank.Ace);
        Assert.Contains("炸弹", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_Rocket_Contains火箭()
    {
        var combo = MakeCombo(CardComboType.Rocket, Rank.BigJoker);
        Assert.Contains("火箭", combo.GetDescription());
    }

    [Fact]
    public void GetDescription_UnknownType_Returns未知()
    {
        // 使用 None 类型（Invalid 的类型），走 default 分支
        var combo = new CardCombo(CardComboType.None, Rank.Three, 0, Array.Empty<Card>());
        Assert.Equal("未知", combo.GetDescription());
    }

    [Fact]
    public void ToString_IncludesDescriptionAndCardCount()
    {
        var cards = new[] { new Card(Suit.Diamonds, Rank.Three) };
        var combo = new CardCombo(CardComboType.Single, Rank.Three, 1, cards);
        Assert.Equal("单张 (1张)", combo.ToString());
    }

    [Fact]
    public void ToString_MultipleCards_ShowsCorrectCount()
    {
        var cards = new[]
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Clubs, Rank.Three)
        };
        var combo = new CardCombo(CardComboType.Bomb, Rank.Three, 1, cards);
        Assert.Contains("4张", combo.ToString());
    }

    [Fact]
    public void Constructor_CardsAreReadOnly()
    {
        var cards = new[] { new Card(Suit.Diamonds, Rank.Ace) };
        var combo = new CardCombo(CardComboType.Single, Rank.Ace, 1, cards);
        Assert.IsAssignableFrom<IReadOnlyList<Card>>(combo.Cards);
    }

    [Fact]
    public void PrimaryRank_IsStored()
    {
        var combo = MakeCombo(CardComboType.Straight, Rank.Seven, chainLen: 5);
        Assert.Equal(Rank.Seven, combo.PrimaryRank);
    }

    [Fact]
    public void ChainLength_IsStored()
    {
        var combo = MakeCombo(CardComboType.ConsecutivePairs, Rank.Four, chainLen: 4);
        Assert.Equal(4, combo.ChainLength);
    }
}
