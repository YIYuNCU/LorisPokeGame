// -----------------------------------------------------------------------
// NetworkGameManager.cs - 联机游戏管理器
// 封装网络通信层，同步游戏状态
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using System.Text.Json;

namespace LolitaPoker.Core.Network;

/// <summary>
/// 联机游戏管理器 - 在本地GameManager之上封装网络同步
/// </summary>
public class NetworkGameManager
{
    private readonly GameManager _gameManager;
    private readonly INetworkAdapter _network;

    public NetworkGameManager(GameManager gameManager, INetworkAdapter network)
    {
        _gameManager = gameManager;
        _network = network;

        _network.OnMessageReceived += HandleNetworkMessage;
    }

    // ========== 联机事件 ==========
    public event Action<string>? GameStateReceived;
    public event Action<string, string, string>? OpponentActionReceived;
    public event Action<string, string>? PlayerJoined;
    public event Action<string>? PlayerLeft;

    // ========== 服务器模式事件（直接转发原始消息） ==========
    public event Action<string, string>? ServerMessageReceived; // (type, payloadJson)

    // ========== 发送到网络的操作 ==========

    public void RequestNewGame()
    {
        var msg = new NetworkMessage
        {
            Type = "NewGame",
            SenderId = _network.LocalPlayerId,
            Payload = "{}"
        };
        _ = _network.SendMessageAsync(msg);
    }

    public void SendBid(int amount)
    {
        var msg = new NetworkMessage
        {
            Type = "Bid",
            SenderId = _network.LocalPlayerId,
            Payload = JsonSerializer.Serialize(new { Amount = amount })
        };
        _ = _network.SendMessageAsync(msg);
    }

    public void SendPlay(List<Card> cards)
    {
        var cardData = cards.Select(c => new { Suit = (int)c.Suit, Rank = (int)c.Rank }).ToArray();
        var msg = new NetworkMessage
        {
            Type = "Play",
            SenderId = _network.LocalPlayerId,
            Payload = JsonSerializer.Serialize(cardData)
        };
        _ = _network.SendMessageAsync(msg);
    }

    public void SendPass()
    {
        var msg = new NetworkMessage
        {
            Type = "Pass",
            SenderId = _network.LocalPlayerId,
            Payload = "{}"
        };
        _ = _network.SendMessageAsync(msg);
    }

    public void SendGameState()
    {
        var state = new
        {
            Phase = (int)_gameManager.Phase,
            CurrentPlayer = _gameManager.CurrentPlayerIndex,
            CurrentBid = _gameManager.CurrentBid,
            LandlordIndex = _gameManager.LandlordIndex,
            PlayerHandCounts = Enumerable.Range(0, 3)
                .Select(i => _gameManager.GetPlayerHand(i).Count)
                .ToArray()
        };
        var msg = new NetworkMessage
        {
            Type = "GameState",
            SenderId = _network.LocalPlayerId,
            Payload = JsonSerializer.Serialize(state)
        };
        _ = _network.SendMessageAsync(msg);
    }

    // ========== 处理网络消息 ==========

    private void HandleNetworkMessage(NetworkMessage message)
    {
        if (message.SenderId == _network.LocalPlayerId) return;

        switch (message.Type)
        {
            case "NewGame":
                _gameManager.StartNewGame();
                break;

            case "Bid":
                var bidData = JsonSerializer.Deserialize<BidPayload>(message.Payload);
                if (bidData != null)
                    _gameManager.SubmitBid(GetPlayerIndex(message.SenderId), bidData.Amount);
                break;

            case "Play":
                var cards = JsonSerializer.Deserialize<CardData[]>(message.Payload);
                if (cards != null)
                {
                    var hand = cards.Select(c => new Card((Suit)c.Suit, (Rank)c.Rank)).ToList();
                    _gameManager.SubmitPlay(GetPlayerIndex(message.SenderId), hand);
                }
                break;

            case "Pass":
                _gameManager.SubmitPass(GetPlayerIndex(message.SenderId));
                break;

            case "GameState":
                GameStateReceived?.Invoke(message.Payload);
                break;

            case "PlayerJoin":
                PlayerJoined?.Invoke(message.SenderId, message.Payload);
                break;

            case "PlayerLeave":
                PlayerLeft?.Invoke(message.SenderId);
                break;

            // 服务器模式：转发所有其他消息给 GameViewModel 处理
            default:
                ServerMessageReceived?.Invoke(message.Type, message.Payload);
                break;
        }
    }

    private int GetPlayerIndex(string playerId)
    {
        // 简化实现：根据网络顺序映射座位
        return 1; // 对手固定为座位1
    }

    private record BidPayload(int Amount);
    private record CardData(int Suit, int Rank);
}
