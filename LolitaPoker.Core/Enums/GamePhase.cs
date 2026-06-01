// -----------------------------------------------------------------------
// GamePhase.cs - 游戏阶段枚举
// -----------------------------------------------------------------------

namespace LolitaPoker.Core.Enums;

/// <summary>游戏阶段</summary>
public enum GamePhase
{
    /// <summary>等待开始</summary>
    Idle,
    /// <summary>发牌中</summary>
    Dealing,
    /// <summary>叫地主/叫分阶段</summary>
    Bidding,
    /// <summary>出牌阶段</summary>
    Playing,
    /// <summary>游戏结束</summary>
    GameOver
}
