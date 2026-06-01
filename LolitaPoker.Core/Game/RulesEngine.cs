// -----------------------------------------------------------------------
// RulesEngine.cs - 规则引擎（牌型识别+合法性判定）
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.Game;

/// <summary>
/// 规则引擎 - 识别牌型、判断合法性
/// </summary>
public static class RulesEngine
{
    /// <summary>
    /// 识别一组牌的牌型
    /// </summary>
    public static CardCombo ClassifyPlay(IReadOnlyList<Card> cards)
    {
        if (cards == null || cards.Count == 0)
            return CardCombo.Invalid;

        // 按点数分组统计频率
        var freq = new Dictionary<Rank, int>();
        foreach (var card in cards)
        {
            freq[card.Rank] = freq.GetValueOrDefault(card.Rank) + 1;
        }

        // 按频率分组
        var counts = new Dictionary<int, List<Rank>>();
        foreach (var (rank, count) in freq)
        {
            if (!counts.ContainsKey(count))
                counts[count] = new List<Rank>();
            counts[count].Add(rank);
        }

        int totalCards = cards.Count;
        var sortedRanks = freq.Keys.OrderBy(r => (int)r).ToList();

        // ========== 特殊牌型优先检测 ==========

        // 火箭：大小王
        if (totalCards == 2 && freq.ContainsKey(Rank.SmallJoker) && freq.ContainsKey(Rank.BigJoker))
            return new CardCombo(CardComboType.Rocket, Rank.BigJoker, 1, cards);

        // 炸弹：四张相同
        if (freq.Count == 1 && freq.Values.First() == 4)
            return new CardCombo(CardComboType.Bomb, freq.Keys.First(), 1, cards);

        // ========== 单张/对子/三条 ==========
        if (freq.Count == 1)
        {
            var rank = freq.Keys.First();
            var count = freq.Values.First();
            return count switch
            {
                1 => new CardCombo(CardComboType.Single, rank, 1, cards),
                2 => new CardCombo(CardComboType.Pair, rank, 1, cards),
                3 => new CardCombo(CardComboType.Triple, rank, 1, cards),
                _ => CardCombo.Invalid
            };
        }

        // ========== 三带一/三带二 ==========
        if (counts.ContainsKey(3) && counts[3].Count == 1)
        {
            var tripleRank = counts[3][0];

            if (counts.ContainsKey(1) && counts[1].Count == 1 && totalCards == 4)
                return new CardCombo(CardComboType.TriplePlusOne, tripleRank, 1, cards);

            if (counts.ContainsKey(2) && counts[2].Count == 1 && totalCards == 5)
                return new CardCombo(CardComboType.TriplePlusPair, tripleRank, 1, cards);
        }

        // ========== 四带二 ==========
        if (counts.ContainsKey(4) && counts[4].Count == 1)
        {
            var fourRank = counts[4][0];

            if (counts.ContainsKey(1) && counts[1].Count == 2 && totalCards == 6)
                return new CardCombo(CardComboType.FourPlusTwo, fourRank, 1, cards);

            if (counts.ContainsKey(2) && counts[2].Count == 2 && totalCards == 8)
                return new CardCombo(CardComboType.FourPlusTwoPairs, fourRank, 1, cards);
        }

        // ========== 顺子（5+张连续单张，不含2和王） ==========
        if (freq.Values.All(v => v == 1) && totalCards >= 5 && CanFormChain(sortedRanks))
        {
            var maxRank = sortedRanks.MaxBy(r => (int)r);
            return new CardCombo(CardComboType.Straight, maxRank, totalCards, cards);
        }

        // ========== 连对（3+组连续对子，不含2和王） ==========
        if (freq.Values.All(v => v == 2) && freq.Count >= 3 && CanFormChain(sortedRanks))
        {
            var maxRank = sortedRanks.MaxBy(r => (int)r);
            return new CardCombo(CardComboType.ConsecutivePairs, maxRank, freq.Count, cards);
        }

        // ========== 飞机（2+组连续三条） ==========
        if (counts.ContainsKey(3) && counts[3].Count >= 2)
        {
            var tripleRanks = counts[3].OrderBy(r => (int)r).ToList();

            if (CanFormChain(tripleRanks))
            {
                var maxRank = tripleRanks.MaxBy(r => (int)r);
                int tripleCount = tripleRanks.Count;
                int nonTripleCards = totalCards - tripleCount * 3;

                if (nonTripleCards == 0)
                    return new CardCombo(CardComboType.Airplane, maxRank, tripleCount, cards);

                if (nonTripleCards == tripleCount && counts.ContainsKey(1) && counts[1].Count == tripleCount)
                    return new CardCombo(CardComboType.AirplaneWithSingles, maxRank, tripleCount, cards);

                if (nonTripleCards == tripleCount * 2 && counts.ContainsKey(2) && counts[2].Count == tripleCount)
                    return new CardCombo(CardComboType.AirplaneWithPairs, maxRank, tripleCount, cards);
            }
        }

        return CardCombo.Invalid;
    }

    /// <summary>
    /// 判断一组点数是否能形成连续序列（3~A范围内连续，不含2和王）
    /// </summary>
    private static bool CanFormChain(List<Rank> sortedRanks)
    {
        if (sortedRanks.Count < 2) return false;

        // 不能包含2和王牌
        foreach (var rank in sortedRanks)
        {
            if (rank == Rank.Two || rank == Rank.SmallJoker || rank == Rank.BigJoker)
                return false;
        }

        // 检查是否连续
        for (int i = 1; i < sortedRanks.Count; i++)
        {
            if ((int)sortedRanks[i] - (int)sortedRanks[i - 1] != 1)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 判断candidate能否压过current
    /// </summary>
    public static bool CanBeat(CardCombo candidate, CardCombo current)
    {
        if (!candidate.IsValid || !current.IsValid) return false;

        // 火箭压一切
        if (candidate.Type == CardComboType.Rocket) return true;

        // 炸弹压非炸弹/火箭
        if (candidate.Type == CardComboType.Bomb)
        {
            if (current.Type == CardComboType.Rocket) return false;
            if (current.Type == CardComboType.Bomb)
                return (int)candidate.PrimaryRank > (int)current.PrimaryRank;
            return true; // 炸弹压非炸弹
        }

        // 同类型比较
        if (candidate.Type != current.Type) return false;
        if (candidate.ChainLength != current.ChainLength) return false;

        return (int)candidate.PrimaryRank > (int)current.PrimaryRank;
    }
}
