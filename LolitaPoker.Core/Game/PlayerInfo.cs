// -----------------------------------------------------------------------
// PlayerInfo.cs - 玩家信息
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.Game;

/// <summary>
/// 玩家信息（游戏逻辑层）
/// </summary>
public class PlayerInfo
{
    public PlayerInfo(int seatIndex, string name, bool isHuman)
    {
        SeatIndex = seatIndex;
        Name = name;
        IsHuman = isHuman;
    }

    public int SeatIndex { get; }
    public string Name { get; }
    public bool IsHuman { get; }
    public PlayerRole Role { get; set; } = PlayerRole.Farmer;
    public List<Card> Hand { get; } = new();
}
