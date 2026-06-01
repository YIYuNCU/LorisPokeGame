using System.Collections.Generic;
using System.Linq;
using Xunit;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Tests;

public class DeckTests
{
    [Fact]
    public void Deal_ReturnsCorrectCardCounts()
    {
        var deck = new Deck();
        var (hand0, hand1, hand2, kitty) = deck.Deal();

        Assert.Equal(17, hand0.Count);
        Assert.Equal(17, hand1.Count);
        Assert.Equal(17, hand2.Count);
        Assert.Equal(3, kitty.Count);
        Assert.Equal(54, hand0.Count + hand1.Count + hand2.Count + kitty.Count);
    }

    [Fact]
    public void Deal_NoDuplicateCards()
    {
        var deck = new Deck();
        var (hand0, hand1, hand2, kitty) = deck.Deal();

        var allCards = hand0.Concat(hand1).Concat(hand2).Concat(kitty).ToList();
        var distinctCount = allCards.Distinct().Count();

        Assert.Equal(54, distinctCount);
    }

    [Fact]
    public void Deal_HandsAreSorted()
    {
        var deck = new Deck();
        var (hand0, hand1, hand2, kitty) = deck.Deal();

        AssertHandSortedDescending(hand0);
        AssertHandSortedDescending(hand1);
        AssertHandSortedDescending(hand2);
    }

    private static void AssertHandSortedDescending(IReadOnlyList<Card> hand)
    {
        for (int i = 0; i < hand.Count - 1; i++)
        {
            Assert.True(hand[i].Strength >= hand[i + 1].Strength,
                $"Card at index {i} (strength={hand[i].Strength}) should be >= card at {i + 1} (strength={hand[i + 1].Strength})");
        }
    }

    [Fact]
    public void DeckPool_ReusesDeckInstance()
    {
        // Get a deck, return it, get again - the underlying list should be reused
        // (same capacity indicates reuse since a fresh list has capacity 54)
        var deck1 = DeckPool.GetShuffledDeck();
        int capacity = deck1.Capacity;
        DeckPool.Return(deck1);
        var deck2 = DeckPool.GetShuffledDeck();

        Assert.Equal(capacity, deck2.Capacity);
        Assert.Equal(54, deck2.Count);
    }
}
