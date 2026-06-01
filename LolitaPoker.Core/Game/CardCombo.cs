// -----------------------------------------------------------------------
// CardCombo.cs - 牌型组合（分类后的出牌方案）
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.Game;

/// <summary>
/// 牌型组合 - 表示一个已分类的出牌方案
/// </summary>
public class CardCombo
{
    public static readonly CardCombo Invalid = new(CardComboType.None, Rank.Three, 0, Array.Empty<Card>());

    public CardCombo(CardComboType type, Rank primaryRank, int chainLength, IEnumerable<Card> cards)
    {
        Type = type;
        PrimaryRank = primaryRank;
        ChainLength = chainLength;
        Cards = cards.ToList().AsReadOnly();
    }

    /// <summary>牌型</summary>
    public CardComboType Type { get; }

    /// <summary>主牌点数（用于比较大小）</summary>
    public Rank PrimaryRank { get; }

    /// <summary>链长度（顺子/连对/飞机的组数）</summary>
    public int ChainLength { get; }

    /// <summary>包含的牌</summary>
    public IReadOnlyList<Card> Cards { get; }

    /// <summary>是否合法牌型</summary>
    public bool IsValid => Type != CardComboType.None;

    /// <summary>获取牌型描述</summary>
    public string GetDescription()
    {
        return Type switch
        {
            CardComboType.Single => "单张",
            CardComboType.Pair => "对子",
            CardComboType.Triple => "三条",
            CardComboType.TriplePlusOne => "三带一",
            CardComboType.TriplePlusPair => "三带二",
            CardComboType.Straight => $"顺子({ChainLength})",
            CardComboType.ConsecutivePairs => $"连对({ChainLength})",
            CardComboType.Airplane => $"飞机({ChainLength})",
            CardComboType.AirplaneWithSingles => $"飞机带单({ChainLength})",
            CardComboType.AirplaneWithPairs => $"飞机带对({ChainLength})",
            CardComboType.FourPlusTwo => "四带二",
            CardComboType.FourPlusTwoPairs => "四带二对",
            CardComboType.Bomb => "炸弹 💣",
            CardComboType.Rocket => "火箭 🚀",
            _ => "未知"
        };
    }

    public override string ToString() => $"{GetDescription()} ({Cards.Count}张)";
}
