// -----------------------------------------------------------------------
// CardSelectionCacheTests.cs - 选牌缓存管理测试
// -----------------------------------------------------------------------

using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.ViewModels;

namespace LolitaPoker.Tests;

public class CardSelectionCacheTests
{
    // ========== 基础操作（无 CardViewModel 依赖） ==========

    [Fact]
    public void ToggleSelection_AddsWhenNotSelected()
    {
        var cache = new CardSelectionCache();
        cache.ToggleSelection(Suit.Diamonds, Rank.Three);
        Assert.True(cache.IsSelected(Suit.Diamonds, Rank.Three));
    }

    [Fact]
    public void ToggleSelection_RemovesWhenSelected()
    {
        var cache = new CardSelectionCache();
        cache.ToggleSelection(Suit.Diamonds, Rank.Three);
        cache.ToggleSelection(Suit.Diamonds, Rank.Three);
        Assert.False(cache.IsSelected(Suit.Diamonds, Rank.Three));
    }

    [Fact]
    public void SetSelection_True_AddsCard()
    {
        var cache = new CardSelectionCache();
        cache.SetSelection(Suit.Hearts, Rank.Ace, true);
        Assert.True(cache.IsSelected(Suit.Hearts, Rank.Ace));
    }

    [Fact]
    public void SetSelection_False_RemovesCard()
    {
        var cache = new CardSelectionCache();
        cache.SetSelection(Suit.Hearts, Rank.Ace, true);
        cache.SetSelection(Suit.Hearts, Rank.Ace, false);
        Assert.False(cache.IsSelected(Suit.Hearts, Rank.Ace));
    }

    [Fact]
    public void IsSelected_NotSelected_ReturnsFalse()
    {
        var cache = new CardSelectionCache();
        Assert.False(cache.IsSelected(Suit.Diamonds, Rank.Three));
    }

    [Fact]
    public void Count_InitiallyZero()
    {
        var cache = new CardSelectionCache();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Count_IncrementsOnSelection()
    {
        var cache = new CardSelectionCache();
        cache.ToggleSelection(Suit.Diamonds, Rank.Three);
        Assert.Equal(1, cache.Count);
        cache.ToggleSelection(Suit.Hearts, Rank.Five);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Clear_RemovesAllSelections()
    {
        var cache = new CardSelectionCache();
        cache.ToggleSelection(Suit.Diamonds, Rank.Three);
        cache.ToggleSelection(Suit.Hearts, Rank.Five);
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.False(cache.IsSelected(Suit.Diamonds, Rank.Three));
    }

    [Fact]
    public void SelectionChanged_FiresOnToggle()
    {
        var cache = new CardSelectionCache();
        int fireCount = 0;
        cache.SelectionChanged += () => fireCount++;
        cache.ToggleSelection(Suit.Diamonds, Rank.Three);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void SelectionChanged_FiresOnClear()
    {
        var cache = new CardSelectionCache();
        int fireCount = 0;
        cache.SelectionChanged += () => fireCount++;
        cache.Clear();
        Assert.Equal(1, fireCount);
    }

    // ========== CardViewModel 交互 ==========

    [Fact]
    public void SyncFromHand_SyncsFromViewModels()
    {
        var cache = new CardSelectionCache();
        var hand = new List<CardViewModel>
        {
            new CardViewModel(new Card(Suit.Diamonds, Rank.Three), true) { IsSelected = true },
            new CardViewModel(new Card(Suit.Hearts, Rank.Five), true) { IsSelected = false },
            new CardViewModel(new Card(Suit.Spades, Rank.Ace), true) { IsSelected = true },
        };

        cache.SyncFromHand(hand);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.IsSelected(Suit.Diamonds, Rank.Three));
        Assert.False(cache.IsSelected(Suit.Hearts, Rank.Five));
        Assert.True(cache.IsSelected(Suit.Spades, Rank.Ace));
    }

    [Fact]
    public void ApplyToHand_SetsViewModelSelection()
    {
        var cache = new CardSelectionCache();
        cache.SetSelection(Suit.Diamonds, Rank.Three, true);
        cache.SetSelection(Suit.Spades, Rank.Ace, true);

        var hand = new List<CardViewModel>
        {
            new CardViewModel(new Card(Suit.Diamonds, Rank.Three), true),
            new CardViewModel(new Card(Suit.Hearts, Rank.Five), true),
            new CardViewModel(new Card(Suit.Spades, Rank.Ace), true),
        };

        cache.ApplyToHand(hand);

        Assert.True(hand[0].IsSelected);
        Assert.False(hand[1].IsSelected);
        Assert.True(hand[2].IsSelected);
    }

    [Fact]
    public void GetSelectedCardModels_ReturnsOnlySelected()
    {
        var cache = new CardSelectionCache();
        cache.SetSelection(Suit.Diamonds, Rank.Three, true);
        cache.SetSelection(Suit.Spades, Rank.Ace, true);

        var hand = new List<CardViewModel>
        {
            new CardViewModel(new Card(Suit.Diamonds, Rank.Three), true),
            new CardViewModel(new Card(Suit.Hearts, Rank.Five), true),
            new CardViewModel(new Card(Suit.Spades, Rank.Ace), true),
        };

        var selected = cache.GetSelectedCardModels(hand);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, c => c.Suit == Suit.Diamonds && c.Rank == Rank.Three);
        Assert.Contains(selected, c => c.Suit == Suit.Spades && c.Rank == Rank.Ace);
    }

    [Fact]
    public void GetSelectedCardModels_EmptyWhenNothingSelected()
    {
        var cache = new CardSelectionCache();
        var hand = new List<CardViewModel>
        {
            new CardViewModel(new Card(Suit.Diamonds, Rank.Three), true),
        };

        var selected = cache.GetSelectedCardModels(hand);

        Assert.Empty(selected);
    }
}
