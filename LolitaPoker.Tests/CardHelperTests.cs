using System.Collections.Generic;
using System.Linq;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Tests;

public class CardHelperTests
{
    [Fact]
    public void CreateFullDeck_Returns54Cards()
    {
        var deck = CardHelper.CreateFullDeck();

        Assert.Equal(54, deck.Count);

        // No duplicates
        var distinct = deck.Distinct().Count();
        Assert.Equal(54, distinct);
    }

    [Fact]
    public void CreateFullDeck_ContainsJokers()
    {
        var deck = CardHelper.CreateFullDeck();

        Assert.Contains(deck, c => c.Suit == Suit.None && c.Rank == Rank.SmallJoker);
        Assert.Contains(deck, c => c.Suit == Suit.None && c.Rank == Rank.BigJoker);
    }

    [Fact]
    public void GetDisplayName_RegularCard()
    {
        var card = new Card(Suit.Diamonds, Rank.Ace);
        Assert.Equal("方片A", CardHelper.GetDisplayName(card));
    }

    [Fact]
    public void GetDisplayName_Joker()
    {
        var small = new Card(Suit.None, Rank.SmallJoker);
        var big = new Card(Suit.None, Rank.BigJoker);

        Assert.Equal("小王", CardHelper.GetDisplayName(small));
        Assert.Equal("大王", CardHelper.GetDisplayName(big));
    }

    [Fact]
    public void GetImageFileName_RegularCard()
    {
        var card = new Card(Suit.Diamonds, Rank.Ace);
        Assert.Equal("方片A.png", CardHelper.GetImageFileName(card));
    }

    [Fact]
    public void SortHand_SortsDescending()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Ace),
            new Card(Suit.Spades, Rank.Two),
            new Card(Suit.Clubs, Rank.Seven),
            new Card(Suit.Diamonds, Rank.King),
        };

        CardHelper.SortHand(hand);

        for (int i = 0; i < hand.Count - 1; i++)
        {
            Assert.True(hand[i].Strength >= hand[i + 1].Strength,
                $"Card at index {i} ({hand[i].Strength}) should be >= card at {i + 1} ({hand[i + 1].Strength})");
        }
    }

    // ========== 补充：覆盖所有花色和大小王 ==========

    [Fact]
    public void GetDisplayName_Hearts_Returns红桃()
    {
        var card = new Card(Suit.Hearts, Rank.King);
        Assert.Equal("红桃K", CardHelper.GetDisplayName(card));
    }

    [Fact]
    public void GetDisplayName_Spades_Returns黑桃()
    {
        var card = new Card(Suit.Spades, Rank.Ten);
        Assert.Equal("黑桃10", CardHelper.GetDisplayName(card));
    }

    [Fact]
    public void GetDisplayName_Clubs_Returns梅花()
    {
        var card = new Card(Suit.Clubs, Rank.Two);
        Assert.Equal("梅花2", CardHelper.GetDisplayName(card));
    }

    [Fact]
    public void GetImageFileName_Joker_Returns大王Png()
    {
        var big = new Card(Suit.None, Rank.BigJoker);
        Assert.Equal("大王.png", CardHelper.GetImageFileName(big));

        var small = new Card(Suit.None, Rank.SmallJoker);
        Assert.Equal("小王.png", CardHelper.GetImageFileName(small));
    }
}
