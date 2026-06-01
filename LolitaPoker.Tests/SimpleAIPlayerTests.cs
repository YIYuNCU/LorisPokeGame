using System.Collections.Generic;
using System.Linq;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.AI;

namespace LolitaPoker.Tests;

public class SimpleAIPlayerTests
{
    private readonly SimpleAIPlayer _ai = new();

    [Fact]
    public void DecideBid_StrongHandBidsThree()
    {
        var hand = new List<Card>
        {
            new Card(Suit.None, Rank.BigJoker),
            new Card(Suit.None, Rank.SmallJoker),
            new Card(Suit.Diamonds, Rank.Two),
            new Card(Suit.Hearts, Rank.Two)
        };

        int bid = _ai.DecideBid(hand, currentHighBid: 0);

        Assert.Equal(3, bid);
    }

    [Fact]
    public void DecideBid_WeakHandPasses()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Clubs, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven),
            new Card(Suit.Hearts, Rank.Eight),
            new Card(Suit.Spades, Rank.Nine),
            new Card(Suit.Clubs, Rank.Ten),
            new Card(Suit.Diamonds, Rank.Jack),
            new Card(Suit.Hearts, Rank.Queen),
            new Card(Suit.Spades, Rank.King)
        };

        int bid = _ai.DecideBid(hand, currentHighBid: 0);

        Assert.Equal(0, bid);
    }

    [Fact]
    public void DecideBid_MustOutbid()
    {
        // Score = Ace(1) + Two(2) = 3, which maps to bid=1.
        // But currentHighBid=2, so must escalate to 3.
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Ace),
            new Card(Suit.Hearts, Rank.Two),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Clubs, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five)
        };

        int bid = _ai.DecideBid(hand, currentHighBid: 2);

        Assert.Equal(3, bid);
    }

    [Fact]
    public void DecideBid_CannotOutbidThree()
    {
        // Strong hand wants to bid 3, but currentHighBid is already 3.
        var hand = new List<Card>
        {
            new Card(Suit.None, Rank.BigJoker),
            new Card(Suit.None, Rank.SmallJoker),
            new Card(Suit.Diamonds, Rank.Two),
            new Card(Suit.Hearts, Rank.Two)
        };

        int bid = _ai.DecideBid(hand, currentHighBid: 3);

        Assert.Equal(0, bid);
    }

    [Fact]
    public void DecidePlay_LeadPrefersSmallestSingle()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Five)
        };

        var (cards, pass) = _ai.DecidePlay(hand, requiredBeat: null,
            isFirstPlay: true, isLandlord: false,
            lastPlayedByIndex: null, myIndex: 0, landlordIndex: null);

        Assert.False(pass);
        Assert.NotNull(cards);
        Assert.Single(cards);
        Assert.Equal(Rank.Three, cards![0].Rank);
    }

    [Fact]
    public void DecidePlay_PassesWhenNoCombos()
    {
        var hand = new List<Card>();

        var (cards, pass) = _ai.DecidePlay(hand, requiredBeat: null,
            isFirstPlay: true, isLandlord: false,
            lastPlayedByIndex: null, myIndex: 0, landlordIndex: null);

        Assert.True(pass);
        Assert.Null(cards);
    }

    // ========== 补充：叫分阈值、队友感知、对子出牌、一次性出完 ==========

    [Fact]
    public void DecideBid_ScoreThree_BidsOne()
    {
        // Ace(1) + Two(2) = 3 -> bid=1
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Ace),
            new Card(Suit.Hearts, Rank.Two),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Clubs, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five)
        };

        int bid = _ai.DecideBid(hand, currentHighBid: 0);

        Assert.Equal(1, bid);
    }

    [Fact]
    public void DecideBid_ScoreFive_BidsTwo()
    {
        // BigJoker(4) + Ace(1) = 5 -> bid=2
        var hand = new List<Card>
        {
            new Card(Suit.None, Rank.BigJoker),
            new Card(Suit.Diamonds, Rank.Ace),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Clubs, Rank.Four),
            new Card(Suit.Diamonds, Rank.Five)
        };

        int bid = _ai.DecideBid(hand, currentHighBid: 0);

        Assert.Equal(2, bid);
    }

    [Fact]
    public void DecidePlay_TeammateLead_PassesWhenHandHasMany()
    {
        // 农民(myIndex=0, landlordIndex=1)，队友(lastPlayedByIndex=2)出的牌
        // 手牌有5张以上，应该不出
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Clubs, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven),
        };
        var target = new CardCombo(CardComboType.Single, Rank.Two, 1,
            new[] { new Card(Suit.Spades, Rank.Two) });

        var (cards, pass) = _ai.DecidePlay(hand, requiredBeat: target,
            isFirstPlay: false, isLandlord: false,
            lastPlayedByIndex: 2, myIndex: 0, landlordIndex: 1);

        Assert.True(pass);
        Assert.Null(cards);
    }

    [Fact]
    public void DecidePlay_LeadPrefersSmallestCombo()
    {
        // 手牌只有两个对子，AI应先出单张（优先级最高），再出对子
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
        };

        var (cards, pass) = _ai.DecidePlay(hand, requiredBeat: null,
            isFirstPlay: true, isLandlord: false,
            lastPlayedByIndex: null, myIndex: 0, landlordIndex: null);

        Assert.False(pass);
        Assert.NotNull(cards);
        // AI优先出单张最小的（Three），而不是对子
        Assert.Single(cards!);
        Assert.Equal(Rank.Three, cards![0].Rank);
    }

    [Fact]
    public void DecidePlay_OneShotPlaysAllCards()
    {
        // 手牌只剩小王和大王，应该一次打出火箭
        var hand = new List<Card>
        {
            new Card(Suit.None, Rank.SmallJoker),
            new Card(Suit.None, Rank.BigJoker),
        };

        var (cards, pass) = _ai.DecidePlay(hand, requiredBeat: null,
            isFirstPlay: true, isLandlord: false,
            lastPlayedByIndex: null, myIndex: 0, landlordIndex: null);

        Assert.False(pass);
        Assert.NotNull(cards);
        Assert.Equal(2, cards!.Count);
    }

    [Fact]
    public void DecidePlay_BeatsWithSmallestCombo()
    {
        // 手牌有对3和对A，目标是对K，应出对A（能压过的最小对子）
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Diamonds, Rank.Ace),
            new Card(Suit.Hearts, Rank.Ace),
        };
        var target = new CardCombo(CardComboType.Pair, Rank.King, 1,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.King),
                new Card(Suit.Hearts, Rank.King)
            });

        var (cards, pass) = _ai.DecidePlay(hand, requiredBeat: target,
            isFirstPlay: false, isLandlord: true,
            lastPlayedByIndex: null, myIndex: 0, landlordIndex: 0);

        Assert.False(pass);
        Assert.NotNull(cards);
        Assert.Equal(2, cards!.Count);
        Assert.True(cards.All(c => c.Rank == Rank.Ace));
    }
}
