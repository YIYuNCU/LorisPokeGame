// -----------------------------------------------------------------------
// SimpleAIPlayer.cs - 带队友感知的规则AI
// 农民AI会配合队友，不会过度攻击友方
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.Game;

namespace LolitaPoker.Core.AI;

/// <summary>
/// AI玩家接口
/// </summary>
public interface IAIPlayer
{
    int DecideBid(IReadOnlyList<Card> hand, int currentHighBid);
    (List<Card>? cards, bool pass) DecidePlay(IReadOnlyList<Card> hand, CardCombo? requiredBeat,
        bool isFirstPlay, bool isLandlord, int? lastPlayedByIndex, int myIndex, int? landlordIndex);
}

/// <summary>
/// 带队友感知的规则AI
/// 农民之间配合，优先攻击地主
/// </summary>
public class SimpleAIPlayer : IAIPlayer
{
    private readonly Random _rng = new();

    public int DecideBid(IReadOnlyList<Card> hand, int currentHighBid)
    {
        int score = EvaluateHandStrength(hand);

        int bid;
        if (score >= 8) bid = 3;
        else if (score >= 5) bid = 2;
        else if (score >= 3) bid = 1;
        else bid = 0;

        // 必须比当前最高叫分高
        if (bid > 0 && bid <= currentHighBid)
            bid = currentHighBid < 3 ? currentHighBid + 1 : 0;

        return bid;
    }

    public (List<Card>? cards, bool pass) DecidePlay(IReadOnlyList<Card> hand, CardCombo? requiredBeat,
        bool isFirstPlay, bool isLandlord, int? lastPlayedByIndex, int myIndex, int? landlordIndex)
    {
        var playableCombos = CardComboFinder.FindAllPlayableCombos(hand, requiredBeat);

        if (playableCombos.Count == 0)
            return (null, true);

        // === 自由出牌（领先） ===
        if (isFirstPlay || requiredBeat == null)
        {
            var chosen = ChooseLeadPlay(hand, playableCombos, isLandlord);
            return (chosen.Cards.ToList(), false);
        }

        // === 需要压牌 ===
        bool isTeammateLead = false;
        if (!isLandlord && landlordIndex.HasValue && lastPlayedByIndex.HasValue)
        {
            // 农民判断：上一手是另一个农民出的
            isTeammateLead = lastPlayedByIndex.Value != landlordIndex.Value
                          && lastPlayedByIndex.Value != myIndex;
        }

        if (isTeammateLead)
        {
            // 队友出的牌：尽量不压，除非手牌很少快要赢了
            if (hand.Count > 3)
                return (null, true); // 不出，让队友继续

            // 手牌<=3张时可以考虑压（快要赢了）
            if (_rng.Next(3) > 0) // 2/3概率不出
                return (null, true);
        }

        // 压牌：选最小的能打过的牌
        var beat = playableCombos[0];

        // 地主手牌很少时，农民要全力压制
        if (!isLandlord && landlordIndex.HasValue)
        {
            // 如果是压地主的牌，优先出
            return (beat.Cards.ToList(), false);
        }

        // 一般情况：选择最弱的出牌
        return (beat.Cards.ToList(), false);
    }

    /// <summary>选择自由出牌策略</summary>
    private CardCombo ChooseLeadPlay(IReadOnlyList<Card> hand, List<CardCombo> combos, bool isLandlord)
    {
        // 优先出单张最小的，然后对子，逐步拆牌
        var singles = combos.Where(c => c.Type == CardComboType.Single).ToList();
        var pairs = combos.Where(c => c.Type == CardComboType.Pair).ToList();
        var triples = combos.Where(c => c.Type == CardComboType.Triple
                                     || c.Type == CardComboType.TriplePlusOne
                                     || c.Type == CardComboType.TriplePlusPair).ToList();

        // 如果只剩一手牌能一次出完，直接出
        var oneShot = combos.FirstOrDefault(c => c.Cards.Count == hand.Count);
        if (oneShot != null) return oneShot;

        // 优先出单张（从小到大）
        if (singles.Count > 0)
            return singles[0];

        // 出对子
        if (pairs.Count > 0)
            return pairs[0];

        // 出三条/三带
        if (triples.Count > 0)
            return triples[0];

        // 其他合法出牌
        return combos[0];
    }

    private int EvaluateHandStrength(IReadOnlyList<Card> hand)
    {
        int score = 0;
        foreach (var card in hand)
        {
            switch (card.Rank)
            {
                case Rank.BigJoker: score += 4; break;
                case Rank.SmallJoker: score += 3; break;
                case Rank.Two: score += 2; break;
                case Rank.Ace: score += 1; break;
            }
        }

        var groups = hand.GroupBy(c => c.Rank);
        foreach (var g in groups)
        {
            if (g.Count() == 4) score += 5;
        }

        return score;
    }
}
