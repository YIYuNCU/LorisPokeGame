// -----------------------------------------------------------------------
// CardHelper.cs - 扑克牌工具类
// 创建牌组、排序、图片文件名映射
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;

namespace LolitaPoker.Core.Models;

public static class CardHelper
{
    /// <summary>花色 → 中文名映射（对应图片文件名前缀）</summary>
    private static readonly Dictionary<Suit, string> SuitNames = new()
    {
        { Suit.Diamonds, "方片" },
        { Suit.Spades,   "黑桃" },
        { Suit.Hearts,   "红桃" },
        { Suit.Clubs,    "梅花" }
    };

    /// <summary>点数 → 中文名/符号映射（对应图片文件名后缀）</summary>
    private static readonly Dictionary<Rank, string> RankNames = new()
    {
        { Rank.Three, "3" },  { Rank.Four, "4" },  { Rank.Five, "5" },
        { Rank.Six,   "6" },  { Rank.Seven, "7" }, { Rank.Eight, "8" },
        { Rank.Nine,  "9" },  { Rank.Ten, "10" },  { Rank.Jack, "J" },
        { Rank.Queen, "Q" },  { Rank.King, "K" },  { Rank.Ace, "A" },
        { Rank.Two,   "2" },  { Rank.SmallJoker, "小王" }, { Rank.BigJoker, "大王" }
    };

    /// <summary>
    /// 创建一副完整的54张扑克牌（52张标准牌 + 大小王）
    /// </summary>
    public static List<Card> CreateFullDeck()
    {
        var deck = new List<Card>(54);

        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            if (suit == Suit.None) continue; // 跳过 Joker 的花色

            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                if (rank == Rank.SmallJoker || rank == Rank.BigJoker) continue;
                deck.Add(new Card(suit, rank));
            }
        }

        // 添加大小王
        deck.Add(new Card(Suit.None, Rank.SmallJoker));
        deck.Add(new Card(Suit.None, Rank.BigJoker));

        return deck;
    }

    /// <summary>
    /// 获取牌对应的图片文件名，如 "方片A.png"、"大王.png"
    /// </summary>
    public static string GetImageFileName(Card card)
    {
        if (card.IsJoker)
        {
            return $"{RankNames[card.Rank]}.png";
        }
        return $"{SuitNames[card.Suit]}{RankNames[card.Rank]}.png";
    }

    /// <summary>
    /// 获取牌的显示名称，如 "方片A"、"大王"
    /// </summary>
    public static string GetDisplayName(Card card)
    {
        if (card.IsJoker)
        {
            return RankNames[card.Rank];
        }
        return $"{SuitNames[card.Suit]}{RankNames[card.Rank]}";
    }

    /// <summary>
    /// 按斗地主规则排序手牌（从大到小，同点数按花色排）
    /// </summary>
    public static void SortHand(List<Card> hand)
    {
        hand.Sort((a, b) =>
        {
            int cmp = b.Strength.CompareTo(a.Strength); // 大的在前
            if (cmp != 0) return cmp;
            return a.Suit.CompareTo(b.Suit); // 同点数按花色
        });
    }
}
