// -----------------------------------------------------------------------
// NetworkGameManagerTests.cs - 联机游戏管理器测试
// -----------------------------------------------------------------------

using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.Network;

namespace LolitaPoker.Tests;

public class NetworkGameManagerTests
{
    /// <summary>手写桩实现，无 Moq 依赖</summary>
    private class StubNetworkAdapter : INetworkAdapter
    {
        public bool IsConnected { get; set; } = true;
        public string LocalPlayerId { get; set; } = "local";
        public List<NetworkMessage> SentMessages { get; } = new();
        public event Action<NetworkMessage>? OnMessageReceived;
        public event Action<bool>? OnConnectionStateChanged;
        public event Action<string, bool>? OnPlayerPresenceChanged;

        public Task<bool> HostGameAsync(string roomId, string hostPlayerName) => Task.FromResult(true);
        public Task<bool> JoinGameAsync(string roomId, string playerName) => Task.FromResult(true);
        public Task SendMessageAsync(NetworkMessage message) { SentMessages.Add(message); return Task.CompletedTask; }
        public Task DisconnectAsync() => Task.CompletedTask;

        public void SimulateMessage(NetworkMessage msg) => OnMessageReceived?.Invoke(msg);
    }

    // ========== 发送方法验证 ==========

    [Fact]
    public void RequestNewGame_SendsNewGameMessage()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        ngm.RequestNewGame();

        Assert.Single(stub.SentMessages);
        Assert.Equal("NewGame", stub.SentMessages[0].Type);
        Assert.Equal("local", stub.SentMessages[0].SenderId);
    }

    [Fact]
    public void SendBid_SendsCorrectPayload()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        ngm.SendBid(2);

        Assert.Single(stub.SentMessages);
        var msg = stub.SentMessages[0];
        Assert.Equal("Bid", msg.Type);
        Assert.Contains("2", msg.Payload);
    }

    [Fact]
    public void SendPlay_SendsCorrectPayload()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        var cards = new List<Card>
        {
            new Card(Suit.Diamonds, Rank.Three),
            new Card(Suit.Hearts, Rank.Ace)
        };
        ngm.SendPlay(cards);

        Assert.Single(stub.SentMessages);
        var msg = stub.SentMessages[0];
        Assert.Equal("Play", msg.Type);
        // Payload 应包含花色和点数
        Assert.Contains("3", msg.Payload);  // Three=3
        Assert.Contains("14", msg.Payload); // Ace=14
    }

    [Fact]
    public void SendPass_SendsPassMessage()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        ngm.SendPass();

        Assert.Single(stub.SentMessages);
        Assert.Equal("Pass", stub.SentMessages[0].Type);
    }

    [Fact]
    public void SendGameState_SendsGameStateMessage()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        gm.StartNewGameQuick();
        var ngm = new NetworkGameManager(gm, stub);

        ngm.SendGameState();

        Assert.Single(stub.SentMessages);
        var msg = stub.SentMessages[0];
        Assert.Equal("GameState", msg.Type);
        Assert.Contains("Phase", msg.Payload);
        Assert.Contains("CurrentPlayer", msg.Payload);
    }

    // ========== 消息接收处理 ==========

    [Fact]
    public void HandleMessage_NewGame_CallsStartNewGame()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        stub.SimulateMessage(new NetworkMessage
        {
            Type = "NewGame",
            SenderId = "remote",
            Payload = "{}"
        });

        Assert.Equal(GamePhase.Bidding, gm.Phase);
    }

    [Fact]
    public void HandleMessage_GameState_FiresEvent()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        string? receivedPayload = null;
        ngm.GameStateReceived += p => receivedPayload = p;

        stub.SimulateMessage(new NetworkMessage
        {
            Type = "GameState",
            SenderId = "remote",
            Payload = "{\"phase\":3}"
        });

        Assert.NotNull(receivedPayload);
        Assert.Contains("phase", receivedPayload);
    }

    [Fact]
    public void HandleMessage_PlayerJoin_FiresEvent()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        string? receivedId = null;
        string? receivedPayload = null;
        ngm.PlayerJoined += (id, p) => { receivedId = id; receivedPayload = p; };

        stub.SimulateMessage(new NetworkMessage
        {
            Type = "PlayerJoin",
            SenderId = "player2",
            Payload = "{\"name\":\"Bob\"}"
        });

        Assert.Equal("player2", receivedId);
        Assert.NotNull(receivedPayload);
    }

    [Fact]
    public void HandleMessage_PlayerLeave_FiresEvent()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        string? receivedId = null;
        ngm.PlayerLeft += id => receivedId = id;

        stub.SimulateMessage(new NetworkMessage
        {
            Type = "PlayerLeave",
            SenderId = "player3",
            Payload = ""
        });

        Assert.Equal("player3", receivedId);
    }

    [Fact]
    public void HandleMessage_UnknownType_FiresServerMessageReceived()
    {
        var stub = new StubNetworkAdapter();
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        string? receivedType = null;
        string? receivedPayload = null;
        ngm.ServerMessageReceived += (t, p) => { receivedType = t; receivedPayload = p; };

        stub.SimulateMessage(new NetworkMessage
        {
            Type = "custom_event",
            SenderId = "server",
            Payload = "{\"data\":42}"
        });

        Assert.Equal("custom_event", receivedType);
        Assert.NotNull(receivedPayload);
    }

    [Fact]
    public void HandleMessage_SelfMessage_IsFiltered()
    {
        var stub = new StubNetworkAdapter { LocalPlayerId = "local" };
        var gm = new GameManager();
        var ngm = new NetworkGameManager(gm, stub);

        // 确保初始状态
        var phaseBefore = gm.Phase;

        // 模拟自己发的消息，应被过滤
        stub.SimulateMessage(new NetworkMessage
        {
            Type = "NewGame",
            SenderId = "local", // 和 LocalPlayerId 相同
            Payload = "{}"
        });

        // 阶段不应变化
        Assert.Equal(phaseBefore, gm.Phase);
    }
}
