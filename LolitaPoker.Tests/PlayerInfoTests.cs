// -----------------------------------------------------------------------
// PlayerInfoTests.cs - 玩家信息实体测试
// -----------------------------------------------------------------------

using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.Game;

namespace LolitaPoker.Tests;

public class PlayerInfoTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var player = new PlayerInfo(2, "Alice", true);
        Assert.Equal(2, player.SeatIndex);
        Assert.Equal("Alice", player.Name);
        Assert.True(player.IsHuman);
    }

    [Fact]
    public void Constructor_AIPlayer_SetsIsHumanFalse()
    {
        var player = new PlayerInfo(1, "AI", false);
        Assert.False(player.IsHuman);
    }

    [Fact]
    public void Role_DefaultsToFarmer()
    {
        var player = new PlayerInfo(0, "P1", true);
        Assert.Equal(PlayerRole.Farmer, player.Role);
    }

    [Fact]
    public void Role_CanBeChangedToLandlord()
    {
        var player = new PlayerInfo(0, "P1", true);
        player.Role = PlayerRole.Landlord;
        Assert.Equal(PlayerRole.Landlord, player.Role);
    }

    [Fact]
    public void Role_CanBeChangedBackToFarmer()
    {
        var player = new PlayerInfo(0, "P1", true);
        player.Role = PlayerRole.Landlord;
        player.Role = PlayerRole.Farmer;
        Assert.Equal(PlayerRole.Farmer, player.Role);
    }

    [Fact]
    public void Hand_InitiallyEmpty()
    {
        var player = new PlayerInfo(0, "P1", true);
        Assert.Empty(player.Hand);
    }

    [Fact]
    public void Hand_CanAddCards()
    {
        var player = new PlayerInfo(0, "P1", true);
        player.Hand.Add(new Card(Suit.Diamonds, Rank.Three));
        player.Hand.Add(new Card(Suit.Hearts, Rank.Ace));
        Assert.Equal(2, player.Hand.Count);
    }

    [Fact]
    public void Hand_CanBeCleared()
    {
        var player = new PlayerInfo(0, "P1", true);
        player.Hand.Add(new Card(Suit.Diamonds, Rank.Three));
        player.Hand.Add(new Card(Suit.Hearts, Rank.Ace));
        player.Hand.Clear();
        Assert.Empty(player.Hand);
    }
}
