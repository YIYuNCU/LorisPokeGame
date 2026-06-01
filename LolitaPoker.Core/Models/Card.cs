// -----------------------------------------------------------------------
// Card.cs - 扑克牌实体
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;

namespace LolitaPoker.Core.Models;

/// <summary>
/// 扑克牌（不可变值类型）
/// </summary>
public readonly record struct Card(Suit Suit, Rank Rank) : IComparable<Card>
{
    /// <summary>游戏强度值（数值越大越强）</summary>
    public int Strength => (int)Rank;

    /// <summary>是否是王牌（大小王）</summary>
    public bool IsJoker => Suit == Suit.None;

    /// <summary>显示名称，如 "方片A"、"大王"</summary>
    public string DisplayName => CardHelper.GetDisplayName(this);

    /// <summary>图片文件名，如 "方片A.png"、"大王.png"</summary>
    public string ImageFileName => CardHelper.GetImageFileName(this);

    public int CompareTo(Card other) => Strength.CompareTo(other.Strength);
}
