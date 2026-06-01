using System.Collections.Generic;
using System.Linq;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.AI;

namespace LolitaPoker.Tests;

public class CardComboFinderTests
{
    [Fact]
    public void FindLeadCombos_IncludesAllSingles()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Five)
        };

        var combos = CardComboFinder.FindAllPlayableCombos(hand, null);

        var singles = combos.Where(c => c.Type == CardComboType.Single).ToList();

        Assert.Equal(3, singles.Count);
        Assert.Contains(singles, c => c.PrimaryRank == Rank.Three);
        Assert.Contains(singles, c => c.PrimaryRank == Rank.Four);
        Assert.Contains(singles, c => c.PrimaryRank == Rank.Five);
    }

    [Fact]
    public void FindLeadCombos_IncludesPairs()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three)
        };

        var combos = CardComboFinder.FindAllPlayableCombos(hand, null);

        var pairs = combos.Where(c => c.Type == CardComboType.Pair).ToList();

        Assert.Single(pairs);
        Assert.Equal(Rank.Three, pairs[0].PrimaryRank);
    }

    [Fact]
    public void FindLeadCombos_IncludesStraights()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Clubs, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven)
        };

        var combos = CardComboFinder.FindAllPlayableCombos(hand, null);

        var straights = combos.Where(c => c.Type == CardComboType.Straight && c.ChainLength == 5).ToList();

        Assert.NotEmpty(straights);
        var straight = straights[0];
        Assert.Equal(Rank.Seven, straight.PrimaryRank);
        Assert.Equal(5, straight.Cards.Count);
    }

    [Fact]
    public void FindBeatingCombos_HigherSingle()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Four),
            new Card(Suit.Hearts, Rank.Ace)
        };
        var target = new CardCombo(CardComboType.Single, Rank.Three, 1,
            new[] { new Card(Suit.Spades, Rank.Three) });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var singles = combos.Where(c => c.Type == CardComboType.Single).ToList();

        Assert.Equal(2, singles.Count);
        Assert.Contains(singles, c => c.PrimaryRank == Rank.Four);
        Assert.Contains(singles, c => c.PrimaryRank == Rank.Ace);
    }

    [Fact]
    public void FindBeatingCombos_BombBeatsAnything()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Three),
            new Card(Suit.Clubs, Rank.Three)
        };
        var target = new CardCombo(CardComboType.Single, Rank.Ace, 1,
            new[] { new Card(Suit.Diamonds, Rank.Ace) });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var bombs = combos.Where(c => c.Type == CardComboType.Bomb).ToList();

        Assert.NotEmpty(bombs);
    }

    [Fact]
    public void FindBeatingCombos_RocketBeatsBomb()
    {
        var hand = new List<Card>
        {
            new Card(Suit.None, Rank.SmallJoker),
            new Card(Suit.None, Rank.BigJoker)
        };
        var target = new CardCombo(CardComboType.Bomb, Rank.Five, 1,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Five),
                new Card(Suit.Hearts, Rank.Five),
                new Card(Suit.Spades, Rank.Five),
                new Card(Suit.Clubs, Rank.Five)
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var rockets = combos.Where(c => c.Type == CardComboType.Rocket).ToList();

        Assert.Single(rockets);
    }

    [Fact]
    public void FindBeatingCombos_CannotBeatRocket()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Ace),
            new Card(Suit.Spades, Rank.Ace),
            new Card(Suit.Hearts, Rank.Ace),
            new Card(Suit.Clubs, Rank.Ace)
        };
        var target = new CardCombo(CardComboType.Rocket, Rank.BigJoker, 1,
            new[] { new Card(Suit.None, Rank.SmallJoker), new Card(Suit.None, Rank.BigJoker) });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Empty(combos);
    }

    [Fact]
    public void ResultsSortedByCost()
    {
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
            new Card(Suit.Spades, Rank.Five)
        };

        var combos = CardComboFinder.FindAllPlayableCombos(hand, null);

        Assert.NotEmpty(combos);
        // First result should be the cheapest combo
        // Cost = rank * count (no bombs/rockets in this hand)
        // Single(3) cost=3, Single(4) cost=4, Single(5) cost=5
        var firstCombo = combos[0];
        Assert.Equal(CardComboType.Single, firstCombo.Type);
        Assert.Equal(Rank.Three, firstCombo.PrimaryRank);
    }

    // ========== 提示系统测试 ==========

    [Fact]
    public void Hint_FreeLead_IncludesSinglesPairsAndStraights()
    {
        // 手牌含多种牌型，自由出牌应全部枚举
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Spades, Rank.Four),
            new Card(Suit.Clubs, Rank.Five),
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Seven),
        };

        var combos = CardComboFinder.FindAllPlayableCombos(hand, null);

        Assert.NotEmpty(combos);
        Assert.Contains(combos, c => c.Type == CardComboType.Single);
        Assert.Contains(combos, c => c.Type == CardComboType.Pair);
        Assert.Contains(combos, c => c.Type == CardComboType.Straight && c.ChainLength == 5);
    }

    [Fact]
    public void Hint_NoValidPlays_ReturnsEmpty()
    {
        // 手牌只有两张小牌，无法击败一个大对子
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Four),
        };
        var target = new CardCombo(CardComboType.Pair, Rank.Ace, 1,
            new[] { new Card(Suit.Diamonds, Rank.Ace), new Card(Suit.Hearts, Rank.Ace) });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Empty(combos);
    }

    [Fact]
    public void Hint_CycleByCost_CheapestFirst()
    {
        // 多个可出的单张，按代价从小到大排列
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Spades, Rank.Ace),
        };
        // 目标是单张4，手牌有 5 和 A 可以压
        var target = new CardCombo(CardComboType.Single, Rank.Four, 1,
            new[] { new Card(Suit.Diamonds, Rank.Four) });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var singles = combos.Where(c => c.Type == CardComboType.Single).ToList();
        Assert.Equal(2, singles.Count);
        // 应按代价排序：5 < A
        Assert.Equal(Rank.Five, singles[0].PrimaryRank);
        Assert.Equal(Rank.Ace, singles[1].PrimaryRank);
    }

    [Fact]
    public void Hint_BombIncludedAsTrumpWhenTargetIsNotBomb()
    {
        // 目标是对子，手牌中有炸弹，炸弹应作为候选
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Seven),
            new Card(Suit.Hearts, Rank.Seven),
            new Card(Suit.Spades, Rank.Seven),
            new Card(Suit.Clubs, Rank.Seven),
        };
        var target = new CardCombo(CardComboType.Pair, Rank.Ace, 1,
            new[] { new Card(Suit.Diamonds, Rank.Ace), new Card(Suit.Hearts, Rank.Ace) });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Contains(combos, c => c.Type == CardComboType.Bomb);
        // 炸弹代价高，应排在后面
        Assert.True(combos.Last().Type == CardComboType.Bomb);
    }

    [Fact]
    public void Hint_RocketIncludedWhenTargetIsBomb()
    {
        // 目标是炸弹，手牌有火箭，应包含火箭
        var hand = new List<Card>
        {
            new Card(Suit.None, Rank.SmallJoker),
            new Card(Suit.None, Rank.BigJoker),
        };
        var target = new CardCombo(CardComboType.Bomb, Rank.Three, 1,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Three),
                new Card(Suit.Hearts, Rank.Three),
                new Card(Suit.Spades, Rank.Three),
                new Card(Suit.Clubs, Rank.Three),
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Contains(combos, c => c.Type == CardComboType.Rocket);
    }

    [Fact]
    public void Hint_TriplePlusOneKickerIsSmallest()
    {
        // 三带一的带牌应选最小的单张
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Ace),
        };

        var combos = CardComboFinder.FindAllPlayableCombos(hand, null);

        var triplePlusOnes = combos.Where(c => c.Type == CardComboType.TriplePlusOne).ToList();
        Assert.NotEmpty(triplePlusOnes);
        // 带牌应该是最小的单张（Three），不是 Ace
        var kicker = triplePlusOnes[0].Cards.First(c => c.Rank != Rank.Five);
        Assert.Equal(Rank.Three, kicker.Rank);
    }

    [Fact]
    public void Hint_PairBeatingCombos_OnlyHigherPairs()
    {
        // 目标是对子5，手牌有对子3、对子8、对子K
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
            new Card(Suit.Diamonds, Rank.Eight),
            new Card(Suit.Hearts, Rank.Eight),
            new Card(Suit.Diamonds, Rank.King),
            new Card(Suit.Hearts, Rank.King),
        };
        var target = new CardCombo(CardComboType.Pair, Rank.Five, 1,
            new[] { new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Five) });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var pairs = combos.Where(c => c.Type == CardComboType.Pair).ToList();
        Assert.Equal(2, pairs.Count);
        Assert.Contains(pairs, c => c.PrimaryRank == Rank.Eight);
        Assert.Contains(pairs, c => c.PrimaryRank == Rank.King);
        // 对子3（rank < 5）不应出现
        Assert.DoesNotContain(pairs, c => c.PrimaryRank == Rank.Three);
    }

    // ========== 补充：复杂牌型跟牌覆盖 ==========

    [Fact]
    public void FindBeatingCombos_HigherTriple()
    {
        // 手牌有三条8，目标是三条5
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Eight),
            new Card(Suit.Hearts, Rank.Eight),
            new Card(Suit.Spades, Rank.Eight),
            new Card(Suit.Diamonds, Rank.Three),
        };
        var target = new CardCombo(CardComboType.Triple, Rank.Five, 1,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Five),
                new Card(Suit.Hearts, Rank.Five),
                new Card(Suit.Spades, Rank.Five)
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var triples = combos.Where(c => c.Type == CardComboType.Triple).ToList();
        Assert.Contains(triples, c => c.PrimaryRank == Rank.Eight);
    }

    [Fact]
    public void FindBeatingCombos_HigherTriplePlusPair()
    {
        // 手牌：三条J + 对子3，目标是三带二7+4
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Jack),
            new Card(Suit.Hearts, Rank.Jack),
            new Card(Suit.Spades, Rank.Jack),
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Three),
        };
        var target = new CardCombo(CardComboType.TriplePlusPair, Rank.Seven, 1,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Seven),
                new Card(Suit.Hearts, Rank.Seven),
                new Card(Suit.Spades, Rank.Seven),
                new Card(Suit.Diamonds, Rank.Four),
                new Card(Suit.Hearts, Rank.Four)
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Contains(combos, c => c.Type == CardComboType.TriplePlusPair && c.PrimaryRank == Rank.Jack);
    }

    [Fact]
    public void FindBeatingCombos_HigherStraight()
    {
        // 手牌有 6-10 顺子，目标是 3-7 顺子
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Seven),
            new Card(Suit.Spades, Rank.Eight),
            new Card(Suit.Clubs, Rank.Nine),
            new Card(Suit.Diamonds, Rank.Ten),
        };
        var target = new CardCombo(CardComboType.Straight, Rank.Seven, 5,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Three),
                new Card(Suit.Hearts, Rank.Four),
                new Card(Suit.Spades, Rank.Five),
                new Card(Suit.Clubs, Rank.Six),
                new Card(Suit.Diamonds, Rank.Seven)
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var straights = combos.Where(c => c.Type == CardComboType.Straight && c.ChainLength == 5).ToList();
        Assert.NotEmpty(straights);
        Assert.Contains(straights, c => c.PrimaryRank == Rank.Ten);
    }

    [Fact]
    public void FindBeatingCombos_HigherConsecutivePairs()
    {
        // 手牌有对子5,6,7，目标是连对3,4,5
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Six),
            new Card(Suit.Diamonds, Rank.Seven),
            new Card(Suit.Hearts, Rank.Seven),
        };
        var target = new CardCombo(CardComboType.ConsecutivePairs, Rank.Five, 3,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Three),
                new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four),
                new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Hearts, Rank.Five),
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        var cp = combos.Where(c => c.Type == CardComboType.ConsecutivePairs).ToList();
        Assert.NotEmpty(cp);
        Assert.Contains(cp, c => c.PrimaryRank == Rank.Seven);
    }

    [Fact]
    public void FindBeatingCombos_AirplaneWithSingles()
    {
        // 手牌：连三条 5,6 + 两个单张带牌
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Five),
            new Card(Suit.Hearts, Rank.Five),
            new Card(Suit.Spades, Rank.Five),
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Six),
            new Card(Suit.Spades, Rank.Six),
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
        };
        var target = new CardCombo(CardComboType.AirplaneWithSingles, Rank.Four, 2,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Four), new Card(Suit.Hearts, Rank.Four),
                new Card(Suit.Spades, Rank.Four),
                new Card(Suit.Diamonds, Rank.Three), new Card(Suit.Hearts, Rank.Three),
                new Card(Suit.Spades, Rank.Three),
                new Card(Suit.Diamonds, Rank.Seven),
                new Card(Suit.Diamonds, Rank.Eight),
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Contains(combos, c => c.Type == CardComboType.AirplaneWithSingles && c.PrimaryRank == Rank.Six);
    }

    [Fact]
    public void FindBeatingCombos_HigherFourPlusTwo()
    {
        // 手牌：四张9 + 两个单张
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Nine),
            new Card(Suit.Hearts, Rank.Nine),
            new Card(Suit.Spades, Rank.Nine),
            new Card(Suit.Clubs, Rank.Nine),
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Diamonds, Rank.Four),
        };
        var target = new CardCombo(CardComboType.FourPlusTwo, Rank.Seven, 1,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Seven), new Card(Suit.Hearts, Rank.Seven),
                new Card(Suit.Spades, Rank.Seven), new Card(Suit.Clubs, Rank.Seven),
                new Card(Suit.Diamonds, Rank.Ten), new Card(Suit.Diamonds, Rank.Jack)
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Contains(combos, c => c.Type == CardComboType.FourPlusTwo && c.PrimaryRank == Rank.Nine);
    }

    [Fact]
    public void FindBeatingCombos_BombBeatsStraight()
    {
        // 手牌有炸弹，目标是顺子
        var hand = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Six),
            new Card(Suit.Hearts, Rank.Six),
            new Card(Suit.Spades, Rank.Six),
            new Card(Suit.Clubs, Rank.Six),
        };
        var target = new CardCombo(CardComboType.Straight, Rank.Seven, 5,
            new Card[]
            {
                new Card(Suit.Diamonds, Rank.Three),
                new Card(Suit.Hearts, Rank.Four),
                new Card(Suit.Spades, Rank.Five),
                new Card(Suit.Clubs, Rank.Six),
                new Card(Suit.Diamonds, Rank.Seven)
            });

        var combos = CardComboFinder.FindAllPlayableCombos(hand, target);

        Assert.Contains(combos, c => c.Type == CardComboType.Bomb);
    }
}
