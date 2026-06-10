// -----------------------------------------------------------------------
// VpetMpTypes.cs - VPet 访客表自定义消息类型常量
// 使用负数避免与 VPet 内置 MSGType (0-4) 冲突
// -----------------------------------------------------------------------

namespace LolitaPoker.Plugin;

/// <summary>
/// 萝莉丝扑克在 VPet 访客表中的自定义 MPMessage Type 常量。
/// MOD 作者可使用非 MSGType 的任意整数，支持负数。
/// </summary>
public static class VpetMpTypes
{
    // === 大厅阶段 ===
    /// <summary>玩家准备</summary>
    public const int GameReady = -100;
    /// <summary>玩家取消准备</summary>
    public const int GameUnready = -101;
    /// <summary>广播：准备状态更新</summary>
    public const int PlayerReadyState = -102;

    // === 游戏生命周期 ===
    /// <summary>房主广播游戏开始（含各玩家手牌）</summary>
    public const int StartGame = -110;
    /// <summary>重置回大厅状态</summary>
    public const int GameReset = -111;

    // === 叫分阶段 ===
    /// <summary>玩家提交叫分</summary>
    public const int Bid = -120;
    /// <summary>广播：叫分结果</summary>
    public const int BidUpdate = -121;
    /// <summary>广播：地主确定 + 底牌</summary>
    public const int LandlordAssigned = -122;

    // === 出牌阶段 ===
    /// <summary>玩家出牌</summary>
    public const int PlayCards = -130;
    /// <summary>玩家不出</summary>
    public const int Pass = -131;
    /// <summary>广播：回合切换</summary>
    public const int TurnChange = -132;
    /// <summary>广播：出牌结果</summary>
    public const int CardsPlayed = -133;
    /// <summary>广播：玩家不出</summary>
    public const int PlayerPassed = -134;

    // === 游戏结束 ===
    /// <summary>广播：胜负结果</summary>
    public const int GameOver = -140;

    // === 错误 ===
    /// <summary>错误消息</summary>
    public const int Error = -150;
}
