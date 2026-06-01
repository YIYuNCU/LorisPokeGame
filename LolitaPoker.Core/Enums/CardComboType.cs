// -----------------------------------------------------------------------
// CardComboType.cs - 牌型枚举
// -----------------------------------------------------------------------

namespace LolitaPoker.Core.Enums;

public enum CardComboType
{
    None = 0,
    Single,
    Pair,
    Triple,
    TriplePlusOne,
    TriplePlusPair,
    Straight,
    ConsecutivePairs,
    Airplane,
    AirplaneWithSingles,
    AirplaneWithPairs,
    FourPlusTwo,
    FourPlusTwoPairs,
    Bomb,
    Rocket
}
