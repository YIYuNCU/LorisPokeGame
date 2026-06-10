// -----------------------------------------------------------------------
// VpetPayloads.cs - VPet 访客表消息载荷类
// 使用 LinePutScript.Converter.Line 特性进行序列化
// -----------------------------------------------------------------------

using LinePutScript.Converter;

namespace LolitaPoker.Plugin;

/// <summary>卡牌数据（用于网络传输）</summary>
public struct CardData
{
    [Line] public int Suit { get; set; }
    [Line] public int Rank { get; set; }
}

/// <summary>游戏开始载荷（定向发送给每个玩家）</summary>
public class StartGamePayload
{
    [Line] public List<CardData> Hand { get; set; } = new();
    [Line] public string[] PlayerNames { get; set; } = Array.Empty<string>();
    [Line] public int PlayerSeat { get; set; }
    [Line] public int Seed { get; set; }
    [Line] public int FirstPlayerIndex { get; set; }
}

/// <summary>叫分载荷</summary>
public class BidPayload
{
    [Line] public int Amount { get; set; }
    [Line] public int Seat { get; set; }
}

/// <summary>叫分结果广播载荷</summary>
public class BidUpdatePayload
{
    [Line] public int Seat { get; set; }
    [Line] public int Amount { get; set; }
    [Line] public bool Success { get; set; }
}

/// <summary>地主确定广播载荷</summary>
public class LandlordAssignedPayload
{
    [Line] public int Seat { get; set; }
    [Line] public int Multiplier { get; set; }
    [Line] public List<CardData> KittyCards { get; set; } = new();
    [Line] public string PlayerName { get; set; } = "";
}

/// <summary>出牌载荷</summary>
public class PlayCardsPayload
{
    [Line] public List<CardData> Cards { get; set; } = new();
    [Line] public int Seat { get; set; }
}

/// <summary>出牌结果广播载荷</summary>
public class CardsPlayedPayload
{
    [Line] public List<CardData> Cards { get; set; } = new();
    [Line] public int Seat { get; set; }
    [Line] public int RemainingCount { get; set; }
}

/// <summary>不出载荷</summary>
public class PassPayload
{
    [Line] public int Seat { get; set; }
}

/// <summary>不出广播载荷</summary>
public class PlayerPassedPayload
{
    [Line] public int Seat { get; set; }
}

/// <summary>回合切换广播载荷</summary>
public class TurnChangePayload
{
    [Line] public int CurrentPlayer { get; set; }
}

/// <summary>游戏结束广播载荷</summary>
public class GameOverPayload
{
    [Line] public int WinnerSeat { get; set; }
    [Line] public string WinnerRole { get; set; } = "";
    [Line] public int Multiplier { get; set; }
}

/// <summary>准备状态广播载荷</summary>
public class ReadyStatePayload
{
    [Line] public int ReadyCount { get; set; }
    [Line] public bool[] ReadySeats { get; set; } = new bool[3];
}

/// <summary>错误载荷</summary>
public class ErrorPayload
{
    [Line] public string Message { get; set; } = "";
}
