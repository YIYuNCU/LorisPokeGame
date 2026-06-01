// -----------------------------------------------------------------------
// Rank.cs - 扑克牌点数枚举
// 枚举数值直接表示游戏强度大小，便于比较
// 斗地主牌力顺序: 3 < 4 < 5 < ... < K < A < 2 < 小王 < 大王
// -----------------------------------------------------------------------

namespace LolitaPoker.Core.Enums;

public enum Rank
{
    Three = 3,
    Four = 4,
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
    Nine = 9,
    Ten = 10,
    Jack = 11,
    Queen = 12,
    King = 13,
    Ace = 14,
    Two = 15,
    SmallJoker = 16,
    BigJoker = 17
}
