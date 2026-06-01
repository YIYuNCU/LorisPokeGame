// -----------------------------------------------------------------------
// CardTests.cs - Card 记录结构体测试
// -----------------------------------------------------------------------

using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Tests;

public class CardTests
{
    [Fact]
    public void Strength_ReturnsRankValue()
    {
        var card = new Card(Suit.Diamonds, Rank.Ace);
        Assert.Equal(14, card.Strength);
    }

    [Fact]
    public void Strength_ReturnsRankValue_BigJoker()
    {
        var card = new Card(Suit.None, Rank.BigJoker);
        Assert.Equal(17, card.Strength);
    }

    [Fact]
    public void IsJoker_SmallJoker_ReturnsTrue()
    {
        var card = new Card(Suit.None, Rank.SmallJoker);
        Assert.True(card.IsJoker);
    }

    [Fact]
    public void IsJoker_BigJoker_ReturnsTrue()
    {
        var card = new Card(Suit.None, Rank.BigJoker);
        Assert.True(card.IsJoker);
    }

    [Fact]
    public void IsJoker_RegularCard_ReturnsFalse()
    {
        var card = new Card(Suit.Hearts, Rank.Five);
        Assert.False(card.IsJoker);
    }

    [Fact]
    public void CompareTo_HigherRank_ReturnsPositive()
    {
        var ace = new Card(Suit.Diamonds, Rank.Ace);
        var three = new Card(Suit.Hearts, Rank.Three);
        Assert.True(ace.CompareTo(three) > 0);
    }

    [Fact]
    public void CompareTo_LowerRank_ReturnsNegative()
    {
        var three = new Card(Suit.Hearts, Rank.Three);
        var ace = new Card(Suit.Diamonds, Rank.Ace);
        Assert.True(three.CompareTo(ace) < 0);
    }

    [Fact]
    public void CompareTo_SameRank_ReturnsZero()
    {
        var a = new Card(Suit.Diamonds, Rank.Two);
        var b = new Card(Suit.Spades, Rank.Two);
        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void RecordEquality_SameCard_ReturnsTrue()
    {
        var a = new Card(Suit.Diamonds, Rank.Three);
        var b = new Card(Suit.Diamonds, Rank.Three);
        Assert.True(a == b);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RecordEquality_DifferentCard_ReturnsFalse()
    {
        var a = new Card(Suit.Diamonds, Rank.Three);
        var b = new Card(Suit.Hearts, Rank.Four);
        Assert.True(a != b);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DisplayName_DelegatesToCardHelper()
    {
        var ace = new Card(Suit.Diamonds, Rank.Ace);
        Assert.Equal(CardHelper.GetDisplayName(ace), ace.DisplayName);

        var joker = new Card(Suit.None, Rank.BigJoker);
        Assert.Equal(CardHelper.GetDisplayName(joker), joker.DisplayName);
    }
}
