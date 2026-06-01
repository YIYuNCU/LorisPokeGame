// -----------------------------------------------------------------------
// CardComboFinder.cs - 搜索所有合法出牌组合
// 供AI和提示系统使用
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.AI;

/// <summary>
/// 出牌组合查找器 - 枚举手牌中所有能压过目标牌型的组合
/// </summary>
public static class CardComboFinder
{
    /// <summary>
    /// 查找所有能压过目标牌型的合法出牌方案
    /// </summary>
    /// <param name="hand">当前手牌</param>
    /// <param name="target">要压过的牌型（null表示自由出牌）</param>
    /// <returns>所有合法出牌方案列表，按"代价"从小到大排序</returns>
    public static List<CardCombo> FindAllPlayableCombos(IReadOnlyList<Card> hand, CardCombo? target)
    {
        var results = new List<CardCombo>();

        if (target == null || !target.IsValid)
        {
            // 自由出牌：枚举所有可能的合法出牌
            FindAllLeadCombos(hand, results);
        }
        else
        {
            // 压牌：找能打败目标的牌
            FindBeatingCombos(hand, target, results);
        }

        // 按代价排序（优先出小牌）
        results.Sort((a, b) =>
        {
            int costA = CalculateComboCost(a);
            int costB = CalculateComboCost(b);
            return costA.CompareTo(costB);
        });

        return results;
    }

    /// <summary>
    /// 查找所有能压过目标牌型的组合
    /// </summary>
    private static void FindBeatingCombos(IReadOnlyList<Card> hand, CardCombo target, List<CardCombo> results)
    {
        var rankGroups = GroupByRank(hand);

        // 1. 尝试用同类型更大的牌去压
        switch (target.Type)
        {
            case CardComboType.Single:
                FindBeatingSingles(hand, rankGroups, target, results);
                break;
            case CardComboType.Pair:
                FindBeatingPairs(rankGroups, target, results);
                break;
            case CardComboType.Triple:
                FindBeatingTriples(rankGroups, target, results);
                break;
            case CardComboType.TriplePlusOne:
                FindBeatingTriplePlusOne(hand, rankGroups, target, results);
                break;
            case CardComboType.TriplePlusPair:
                FindBeatingTriplePlusPair(hand, rankGroups, target, results);
                break;
            case CardComboType.Straight:
                FindBeatingStraights(rankGroups, target, results);
                break;
            case CardComboType.ConsecutivePairs:
                FindBeatingConsecutivePairs(rankGroups, target, results);
                break;
            case CardComboType.Airplane:
            case CardComboType.AirplaneWithSingles:
            case CardComboType.AirplaneWithPairs:
                FindBeatingAirplanes(hand, rankGroups, target, results);
                break;
            case CardComboType.FourPlusTwo:
            case CardComboType.FourPlusTwoPairs:
                FindBeatingFourPlusX(hand, rankGroups, target, results);
                break;
            case CardComboType.Bomb:
                FindBeatingBombs(rankGroups, target, results);
                break;
            case CardComboType.Rocket:
                // 火箭无法被压
                break;
        }

        // 2. 无论目标是什么牌型，炸弹和火箭总是可以压（如果目标不是炸弹/火箭）
        if (target.Type != CardComboType.Bomb && target.Type != CardComboType.Rocket)
        {
            AddAllBombs(rankGroups, results);
        }

        // 3. 火箭总是可以压任何牌型
        AddRocketIfAvailable(hand, results);
    }

    #region 压牌搜索 - 各牌型

    private static void FindBeatingSingles(IReadOnlyList<Card> hand,
        Dictionary<Rank, List<Card>> groups, CardCombo target, List<CardCombo> results)
    {
        foreach (var (rank, cards) in groups)
        {
            if ((int)rank > (int)target.PrimaryRank)
            {
                results.Add(new CardCombo(CardComboType.Single, rank, 1, new[] { cards[0] }));
            }
        }
    }

    private static void FindBeatingPairs(Dictionary<Rank, List<Card>> groups,
        CardCombo target, List<CardCombo> results)
    {
        foreach (var (rank, cards) in groups)
        {
            if ((int)rank > (int)target.PrimaryRank && cards.Count >= 2)
            {
                results.Add(new CardCombo(CardComboType.Pair, rank, 1, cards.Take(2)));
            }
        }
    }

    private static void FindBeatingTriples(Dictionary<Rank, List<Card>> groups,
        CardCombo target, List<CardCombo> results)
    {
        foreach (var (rank, cards) in groups)
        {
            if ((int)rank > (int)target.PrimaryRank && cards.Count >= 3)
            {
                results.Add(new CardCombo(CardComboType.Triple, rank, 1, cards.Take(3)));
            }
        }
    }

    private static void FindBeatingTriplePlusOne(IReadOnlyList<Card> hand,
        Dictionary<Rank, List<Card>> groups, CardCombo target, List<CardCombo> results)
    {
        foreach (var (tripleRank, tripleCards) in groups)
        {
            if ((int)tripleRank > (int)target.PrimaryRank && tripleCards.Count >= 3)
            {
                // 找最小的单张作为带牌
                var kicker = hand.Where(c => c.Rank != tripleRank)
                    .OrderBy(c => (int)c.Rank)
                    .FirstOrDefault();
                if (kicker.Rank != 0)
                {
                    var play = tripleCards.Take(3).Concat(new[] { kicker });
                    results.Add(new CardCombo(CardComboType.TriplePlusOne, tripleRank, 1, play));
                }
            }
        }
    }

    private static void FindBeatingTriplePlusPair(IReadOnlyList<Card> hand,
        Dictionary<Rank, List<Card>> groups, CardCombo target, List<CardCombo> results)
    {
        foreach (var (tripleRank, tripleCards) in groups)
        {
            if ((int)tripleRank > (int)target.PrimaryRank && tripleCards.Count >= 3)
            {
                // 找最小的对子作为带牌
                var smallestPair = groups
                    .Where(kv => kv.Key != tripleRank && kv.Value.Count >= 2)
                    .OrderBy(kv => (int)kv.Key)
                    .FirstOrDefault();
                if (smallestPair.Value != null)
                {
                    var play = tripleCards.Take(3).Concat(smallestPair.Value.Take(2));
                    results.Add(new CardCombo(CardComboType.TriplePlusPair, tripleRank, 1, play));
                }
            }
        }
    }

    private static void FindBeatingStraights(Dictionary<Rank, List<Card>> groups,
        CardCombo target, List<CardCombo> results)
    {
        int length = target.ChainLength;
        int minRank = (int)target.PrimaryRank - length + 1;

        // 搜索所有可能的顺子
        for (int startRank = minRank + 1; startRank + length - 1 <= (int)Rank.Ace; startRank++)
        {
            bool valid = true;
            var cards = new List<Card>();

            for (int r = startRank; r < startRank + length; r++)
            {
                if (!groups.ContainsKey((Rank)r))
                {
                    valid = false;
                    break;
                }
                cards.Add(groups[(Rank)r][0]);
            }

            if (valid)
            {
                var primaryRank = (Rank)(startRank + length - 1);
                results.Add(new CardCombo(CardComboType.Straight, primaryRank, length, cards));
            }
        }
    }

    private static void FindBeatingConsecutivePairs(Dictionary<Rank, List<Card>> groups,
        CardCombo target, List<CardCombo> results)
    {
        int pairCount = target.ChainLength;
        int minRank = (int)target.PrimaryRank - pairCount + 1;

        for (int startRank = minRank + 1; startRank + pairCount - 1 <= (int)Rank.Ace; startRank++)
        {
            bool valid = true;
            var cards = new List<Card>();

            for (int r = startRank; r < startRank + pairCount; r++)
            {
                if (!groups.ContainsKey((Rank)r) || groups[(Rank)r].Count < 2)
                {
                    valid = false;
                    break;
                }
                cards.AddRange(groups[(Rank)r].Take(2));
            }

            if (valid)
            {
                var primaryRank = (Rank)(startRank + pairCount - 1);
                results.Add(new CardCombo(CardComboType.ConsecutivePairs, primaryRank, pairCount, cards));
            }
        }
    }

    private static void FindBeatingAirplanes(IReadOnlyList<Card> hand,
        Dictionary<Rank, List<Card>> groups, CardCombo target, List<CardCombo> results)
    {
        int tripleCount = target.ChainLength;
        int minRank = (int)target.PrimaryRank - tripleCount + 1;

        // 搜索连续三条
        for (int startRank = minRank + 1; startRank + tripleCount - 1 <= (int)Rank.Ace; startRank++)
        {
            bool valid = true;
            var tripleCards = new List<Card>();

            for (int r = startRank; r < startRank + tripleCount; r++)
            {
                if (!groups.ContainsKey((Rank)r) || groups[(Rank)r].Count < 3)
                {
                    valid = false;
                    break;
                }
                tripleCards.AddRange(groups[(Rank)r].Take(3));
            }

            if (!valid) continue;

            var primaryRank = (Rank)(startRank + tripleCount - 1);

            if (target.Type == CardComboType.Airplane)
            {
                results.Add(new CardCombo(CardComboType.Airplane, primaryRank, tripleCount, tripleCards));
            }
            else if (target.Type == CardComboType.AirplaneWithSingles)
            {
                // 找tripleCount张单牌作为带牌
                var usedRanks = new HashSet<Rank>();
                for (int r = startRank; r < startRank + tripleCount; r++)
                    usedRanks.Add((Rank)r);

                var kickers = hand.Where(c => !usedRanks.Contains(c.Rank))
                    .OrderBy(c => (int)c.Rank)
                    .Take(tripleCount).ToList();
                if (kickers.Count == tripleCount)
                {
                    var play = tripleCards.Concat(kickers);
                    results.Add(new CardCombo(CardComboType.AirplaneWithSingles, primaryRank, tripleCount, play));
                }
            }
            else if (target.Type == CardComboType.AirplaneWithPairs)
            {
                var usedRanks = new HashSet<Rank>();
                for (int r = startRank; r < startRank + tripleCount; r++)
                    usedRanks.Add((Rank)r);

                var pairKickers = new List<Card>();
                foreach (var (rank, cards) in groups)
                {
                    if (!usedRanks.Contains(rank) && cards.Count >= 2 && pairKickers.Count < tripleCount * 2)
                    {
                        pairKickers.AddRange(cards.Take(2));
                    }
                }

                if (pairKickers.Count >= tripleCount * 2)
                {
                    var play = tripleCards.Concat(pairKickers.Take(tripleCount * 2));
                    results.Add(new CardCombo(CardComboType.AirplaneWithPairs, primaryRank, tripleCount, play));
                }
            }
        }
    }

    private static void FindBeatingFourPlusX(IReadOnlyList<Card> hand,
        Dictionary<Rank, List<Card>> groups, CardCombo target, List<CardCombo> results)
    {
        foreach (var (rank, cards) in groups)
        {
            if ((int)rank > (int)target.PrimaryRank && cards.Count == 4)
            {
                if (target.Type == CardComboType.FourPlusTwo)
                {
                    var kickers = hand.Where(c => c.Rank != rank)
                        .OrderBy(c => (int)c.Rank)
                        .Take(2).ToList();
                    if (kickers.Count == 2)
                    {
                        var play = cards.Concat(kickers);
                        results.Add(new CardCombo(CardComboType.FourPlusTwo, rank, 1, play));
                    }
                }
                else // FourPlusTwoPairs
                {
                    var pairKickers = new List<Card>();
                    foreach (var (kr, kc) in groups)
                    {
                        if (kr != rank && kc.Count >= 2 && pairKickers.Count < 4)
                        {
                            pairKickers.AddRange(kc.Take(2));
                        }
                    }
                    if (pairKickers.Count >= 4)
                    {
                        var play = cards.Concat(pairKickers.Take(4));
                        results.Add(new CardCombo(CardComboType.FourPlusTwoPairs, rank, 1, play));
                    }
                }
            }
        }
    }

    private static void FindBeatingBombs(Dictionary<Rank, List<Card>> groups,
        CardCombo target, List<CardCombo> results)
    {
        foreach (var (rank, cards) in groups)
        {
            if ((int)rank > (int)target.PrimaryRank && cards.Count == 4)
            {
                results.Add(new CardCombo(CardComboType.Bomb, rank, 1, cards));
            }
        }
    }

    #endregion

    #region 自由出牌搜索

    /// <summary>自由出牌：枚举所有可能的合法出牌（简化版，仅枚举常见牌型）</summary>
    private static void FindAllLeadCombos(IReadOnlyList<Card> hand, List<CardCombo> results)
    {
        var rankGroups = GroupByRank(hand);

        // 单张
        foreach (var (rank, cards) in rankGroups)
        {
            results.Add(new CardCombo(CardComboType.Single, rank, 1, new[] { cards[0] }));
        }

        // 对子
        foreach (var (rank, cards) in rankGroups)
        {
            if (cards.Count >= 2)
                results.Add(new CardCombo(CardComboType.Pair, rank, 1, cards.Take(2)));
        }

        // 三条、三带一、三带二
        foreach (var (rank, cards) in rankGroups)
        {
            if (cards.Count >= 3)
            {
                results.Add(new CardCombo(CardComboType.Triple, rank, 1, cards.Take(3)));

                var kicker = hand.Where(c => c.Rank != rank)
                    .OrderBy(c => (int)c.Rank)
                    .FirstOrDefault();
                if (kicker.Rank != 0)
                    results.Add(new CardCombo(CardComboType.TriplePlusOne, rank, 1,
                        cards.Take(3).Concat(new[] { kicker })));

                var smallestPair = rankGroups
                    .Where(kv => kv.Key != rank && kv.Value.Count >= 2)
                    .OrderBy(kv => (int)kv.Key)
                    .FirstOrDefault();
                if (smallestPair.Value != null)
                {
                    results.Add(new CardCombo(CardComboType.TriplePlusPair, rank, 1,
                        cards.Take(3).Concat(smallestPair.Value.Take(2))));
                }
            }
        }

        // 炸弹
        foreach (var (rank, cards) in rankGroups)
        {
            if (cards.Count == 4)
                results.Add(new CardCombo(CardComboType.Bomb, rank, 1, cards));
        }

        // 火箭
        AddRocketIfAvailable(hand, results);

        // 顺子 (5+张)
        FindLeadStraights(rankGroups, results);

        // 连对 (3+对)
        FindLeadConsecutivePairs(rankGroups, results);
    }

    private static void FindLeadStraights(Dictionary<Rank, List<Card>> groups, List<CardCombo> results)
    {
        var validRanks = Enum.GetValues<Rank>()
            .Where(r => (int)r >= (int)Rank.Three && (int)r <= (int)Rank.Ace)
            .OrderBy(r => (int)r)
            .ToList();

        for (int len = 5; len <= 12; len++)
        {
            for (int i = 0; i <= validRanks.Count - len; i++)
            {
                bool valid = true;
                var cards = new List<Card>();

                for (int j = 0; j < len; j++)
                {
                    var rank = validRanks[i + j];
                    if (!groups.ContainsKey(rank))
                    {
                        valid = false;
                        break;
                    }
                    cards.Add(groups[rank][0]);
                }

                if (valid)
                {
                    var primaryRank = validRanks[i + len - 1];
                    results.Add(new CardCombo(CardComboType.Straight, primaryRank, len, cards));
                }
            }
        }
    }

    private static void FindLeadConsecutivePairs(Dictionary<Rank, List<Card>> groups, List<CardCombo> results)
    {
        var validRanks = Enum.GetValues<Rank>()
            .Where(r => (int)r >= (int)Rank.Three && (int)r <= (int)Rank.Ace)
            .OrderBy(r => (int)r)
            .ToList();

        for (int pairCount = 3; pairCount <= 10; pairCount++)
        {
            for (int i = 0; i <= validRanks.Count - pairCount; i++)
            {
                bool valid = true;
                var cards = new List<Card>();

                for (int j = 0; j < pairCount; j++)
                {
                    var rank = validRanks[i + j];
                    if (!groups.ContainsKey(rank) || groups[rank].Count < 2)
                    {
                        valid = false;
                        break;
                    }
                    cards.AddRange(groups[rank].Take(2));
                }

                if (valid)
                {
                    var primaryRank = validRanks[i + pairCount - 1];
                    results.Add(new CardCombo(CardComboType.ConsecutivePairs, primaryRank, pairCount, cards));
                }
            }
        }
    }

    #endregion

    #region 辅助方法

    private static void AddAllBombs(Dictionary<Rank, List<Card>> groups, List<CardCombo> results)
    {
        foreach (var (rank, cards) in groups)
        {
            if (cards.Count == 4)
                results.Add(new CardCombo(CardComboType.Bomb, rank, 1, cards));
        }
    }

    private static void AddRocketIfAvailable(IReadOnlyList<Card> hand, List<CardCombo> results)
    {
        var smallJoker = hand.FirstOrDefault(c => c.Rank == Rank.SmallJoker);
        var bigJoker = hand.FirstOrDefault(c => c.Rank == Rank.BigJoker);

        if (smallJoker.Rank != 0 && bigJoker.Rank != 0)
        {
            results.Add(new CardCombo(CardComboType.Rocket, Rank.BigJoker, 1,
                new[] { smallJoker, bigJoker }));
        }
    }

    private static Dictionary<Rank, List<Card>> GroupByRank(IReadOnlyList<Card> hand)
    {
        var groups = new Dictionary<Rank, List<Card>>();
        foreach (var card in hand)
        {
            if (!groups.ContainsKey(card.Rank))
                groups[card.Rank] = new List<Card>();
            groups[card.Rank].Add(card);
        }
        return groups;
    }

    /// <summary>计算出牌"代价"（用于AI选择最优出牌）</summary>
    private static int CalculateComboCost(CardCombo combo)
    {
        int cost = (int)combo.PrimaryRank * combo.Cards.Count;

        // 炸弹和火箭代价很高
        if (combo.Type == CardComboType.Bomb) cost += 100;
        if (combo.Type == CardComboType.Rocket) cost += 200;

        return cost;
    }

    #endregion
}
