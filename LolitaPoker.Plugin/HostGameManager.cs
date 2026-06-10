// -----------------------------------------------------------------------
// HostGameManager.cs - 房主端游戏权威管理器
// 封装本地 GameManager，将事件翻译为 MPMessage 广播
// -----------------------------------------------------------------------

using System.Diagnostics;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Plugin;

/// <summary>
/// 房主端游戏权威。
/// 房主运行本地 GameManager 验证所有操作，
/// 将结果通过 VPetNetworkAdapter 广播给其他玩家。
/// </summary>
public class HostGameManager : IDisposable
{
    private readonly GameManager _gameManager;
    private readonly VPetNetworkAdapter _adapter;
    private readonly string[] _playerNames = new string[3];
    private bool _disposed;

    public GameManager Game => _gameManager;

    public HostGameManager(VPetNetworkAdapter adapter, string hostName)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _gameManager = new GameManager();

        // 订阅 GameManager 事件
        _gameManager.PhaseChanged += OnPhaseChanged;
        _gameManager.TurnChanged += OnTurnChanged;
        _gameManager.PlayerPlayed += OnPlayerPlayed;
        _gameManager.CardsChanged += OnCardsChanged;
        _gameManager.GameEnded += OnGameEnded;
        _gameManager.MessageChanged += OnMessageChanged;

        _playerNames[0] = hostName;
    }

    /// <summary>
    /// 设置玩家名称
    /// </summary>
    public void SetPlayerName(int seat, string name)
    {
        if (seat >= 0 && seat < 3)
            _playerNames[seat] = name;
    }

    /// <summary>
    /// 开始新游戏：使用确定性种子发牌，确保所有玩家手牌一致
    /// </summary>
    public void StartNewGame()
    {
        var seed = Random.Shared.Next();
        var firstPlayer = Random.Shared.Next(3);

        _gameManager.StartNewGame(seed, firstPlayer);

        // 向每个玩家发送种子和玩家名（手牌由种子确定性生成）
        for (int seat = 0; seat < 3; seat++)
        {
            var steamId = _adapter.GetSteamId(seat);
            if (steamId == 0) continue;

            var payload = new StartGamePayload
            {
                Hand = new(), // 非房主通过种子自行生成手牌
                PlayerNames = _playerNames.ToArray(),
                PlayerSeat = seat,
                Seed = seed,
                FirstPlayerIndex = firstPlayer
            };

            _adapter.SendPayloadMessage(VpetMpTypes.StartGame, payload, steamId);
        }

        Debug.WriteLine($"[HostGameManager] 游戏开始 (seed={seed}, first={firstPlayer})");
    }

    /// <summary>
    /// 处理来自玩家的叫分请求
    /// </summary>
    public void HandleBid(int seat, int amount)
    {
        if (_gameManager.Phase != GamePhase.Bidding) return;
        if (_gameManager.CurrentPlayerIndex != seat) return;

        _gameManager.SubmitBid(seat, amount);
    }

    /// <summary>
    /// 处理来自玩家的出牌请求
    /// </summary>
    public void HandlePlay(int seat, List<CardData> cards)
    {
        if (_gameManager.Phase != GamePhase.Playing) return;
        if (_gameManager.CurrentPlayerIndex != seat) return;

        var hand = cards.Select(c => new Card((Suit)c.Suit, (Rank)c.Rank)).ToList();
        _gameManager.SubmitPlay(seat, hand);
    }

    /// <summary>
    /// 处理来自玩家的不出请求
    /// </summary>
    public void HandlePass(int seat)
    {
        if (_gameManager.Phase != GamePhase.Playing) return;
        if (_gameManager.CurrentPlayerIndex != seat) return;

        _gameManager.SubmitPass(seat);
    }

    // ========== GameManager 事件 → 广播 ==========

    private void OnPhaseChanged(GamePhase phase)
    {
        Debug.WriteLine($"[HostGameManager] 阶段变更: {phase}");

        if (phase == GamePhase.Playing && _gameManager.LandlordIndex.HasValue)
        {
            // 广播地主确定
            var kittyCards = _gameManager.KittyCards
                .Select(c => new CardData { Suit = (int)c.Suit, Rank = (int)c.Rank })
                .ToList();

            var landlordPayload = new LandlordAssignedPayload
            {
                Seat = _gameManager.LandlordIndex.Value,
                Multiplier = _gameManager.Multiplier,
                KittyCards = kittyCards,
                PlayerName = _playerNames[_gameManager.LandlordIndex.Value]
            };

            _adapter.SendPayloadMessageAll(VpetMpTypes.LandlordAssigned, landlordPayload);
        }
    }

    private void OnTurnChanged(int playerIndex)
    {
        Debug.WriteLine($"[HostGameManager] 回合切换: 玩家 {playerIndex}");

        var payload = new TurnChangePayload { CurrentPlayer = playerIndex };
        _adapter.SendPayloadMessageAll(VpetMpTypes.TurnChange, payload);
    }

    private void OnPlayerPlayed(int playerIndex, CardCombo? combo)
    {
        if (combo == null)
        {
            // 不出
            var passPayload = new PlayerPassedPayload { Seat = playerIndex };
            _adapter.SendPayloadMessageAll(VpetMpTypes.PlayerPassed, passPayload);
        }
        else
        {
            // 出牌
            var remainingCount = _gameManager.GetPlayerHand(playerIndex).Count;
            var cardsPayload = new CardsPlayedPayload
            {
                Cards = combo.Cards.Select(c => new CardData { Suit = (int)c.Suit, Rank = (int)c.Rank }).ToList(),
                Seat = playerIndex,
                RemainingCount = remainingCount
            };
            _adapter.SendPayloadMessageAll(VpetMpTypes.CardsPlayed, cardsPayload);
        }
    }

    private void OnCardsChanged(int playerIndex)
    {
        // 手牌变化由 PlayerPlayed 事件覆盖，此处无需额外广播
    }

    private void OnGameEnded(int? winnerIndex, int multiplier)
    {
        Debug.WriteLine($"[HostGameManager] 游戏结束: 赢家={winnerIndex}, 倍数={multiplier}");

        string winnerRole = "";
        if (winnerIndex.HasValue)
        {
            winnerRole = _gameManager.LandlordIndex == winnerIndex ? "地主" : "农民";
        }

        var payload = new GameOverPayload
        {
            WinnerSeat = winnerIndex ?? -1,
            WinnerRole = winnerRole,
            Multiplier = multiplier
        };
        _adapter.SendPayloadMessageAll(VpetMpTypes.GameOver, payload);
    }

    private void OnMessageChanged(string message)
    {
        // 消息变化仅用于本地 UI，无需广播
    }

    // ========== IDisposable ==========

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _gameManager.PhaseChanged -= OnPhaseChanged;
        _gameManager.TurnChanged -= OnTurnChanged;
        _gameManager.PlayerPlayed -= OnPlayerPlayed;
        _gameManager.CardsChanged -= OnCardsChanged;
        _gameManager.GameEnded -= OnGameEnded;
        _gameManager.MessageChanged -= OnMessageChanged;

        Debug.WriteLine("[HostGameManager] 已释放资源");
    }
}
