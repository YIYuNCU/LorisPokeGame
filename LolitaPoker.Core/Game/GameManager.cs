// -----------------------------------------------------------------------
// GameManager.cs - 游戏状态机管理器
// 支持快速模式（轮流地主，跳过叫分）
// -----------------------------------------------------------------------

using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Models;

namespace LolitaPoker.Core.Game;

/// <summary>
/// 游戏管理器 - 状态机驱动斗地主游戏流程
/// </summary>
public class GameManager
{
    // ========== 游戏状态 ==========
    private readonly PlayerInfo[] _players = new PlayerInfo[3];
    private List<Card> _kittyCards = new();
    private GamePhase _phase = GamePhase.Idle;
    private int _currentPlayerIndex;
    private CardCombo? _lastPlayedCombo;
    private int? _lastPlayedByIndex;
    private int _consecutivePasses;
    private int _currentBid;
    private int? _highestBidder;
    private int _bidRound;
    private int _multiplier = 1;
    private int _lastLandlordIndex = -1; // 上一局地主（用于轮流）

    public GameManager()
    {
        for (int i = 0; i < 3; i++)
        {
            _players[i] = new PlayerInfo(i, i == 0 ? "玩家" : $"电脑{(i == 1 ? 'A' : 'B')}", i == 0);
        }
    }

    // ========== 公开属性 ==========
    public GamePhase Phase => _phase;
    public int CurrentPlayerIndex => _currentPlayerIndex;
    public CardCombo? LastPlayedCombo => _lastPlayedCombo;
    public int? LastPlayedByIndex => _lastPlayedByIndex;
    public int CurrentBid => _currentBid;
    public int? HighestBidder => _highestBidder;
    public int? LandlordIndex { get; private set; }
    public IReadOnlyList<Card> KittyCards => _kittyCards;
    public int Multiplier => _multiplier;

    // ========== 事件 ==========
    public event Action<GamePhase>? PhaseChanged;
    public event Action<int>? TurnChanged;
    public event Action<int, CardCombo?>? PlayerPlayed;
    public event Action<int>? CardsChanged;
    public event Action<int?, int>? GameEnded;
    public event Action<string>? MessageChanged;

    // ========== 手牌访问 ==========
    public IReadOnlyList<Card> GetPlayerHand(int index) => _players[index].Hand;

    // ========== 发牌（公共步骤） ==========
    private void DealAndReset(int? seed = null)
    {
        _lastPlayedCombo = null;
        _lastPlayedByIndex = null;
        _consecutivePasses = 0;
        _currentBid = 0;
        _highestBidder = null;
        LandlordIndex = null;
        _bidRound = 0;
        _multiplier = 1;

        foreach (var p in _players)
        {
            p.Role = PlayerRole.Farmer;
            p.Hand.Clear();
        }

        var deck = seed.HasValue ? new Deck(seed.Value) : new Deck();
        var (h0, h1, h2, kitty) = deck.Deal();
        deck.ReturnToPool(); // 归还牌组到缓存池

        _players[0].Hand.AddRange(h0);
        _players[1].Hand.AddRange(h1);
        _players[2].Hand.AddRange(h2);
        _kittyCards = kitty;

        SetPhase(GamePhase.Dealing);
        for (int i = 0; i < 3; i++)
            CardsChanged?.Invoke(i);
    }

    /// <summary>
    /// 快速开始：轮流地主，跳过叫分阶段
    /// </summary>
    public void StartNewGameQuick()
    {
        DealAndReset();

        // 轮流地主：上一局地主的下一位
        int landlord = (_lastLandlordIndex + 1) % 3;
        _lastLandlordIndex = landlord;

        AssignLandlord(landlord);
    }

    /// <summary>
    /// 传统模式：叫地主
    /// </summary>
    public void StartNewGame()
    {
        DealAndReset();

        _currentPlayerIndex = Random.Shared.Next(3);
        SetPhase(GamePhase.Bidding);
        TurnChanged?.Invoke(_currentPlayerIndex);
    }

    /// <summary>
    /// 使用确定性种子开始游戏（VPet 联机模式专用，确保所有玩家发牌一致）
    /// </summary>
    public void StartNewGame(int seed, int firstPlayerIndex)
    {
        DealAndReset(seed);

        _currentPlayerIndex = firstPlayerIndex;
        SetPhase(GamePhase.Bidding);
        TurnChanged?.Invoke(_currentPlayerIndex);
    }

    // ========== 叫地主 ==========
    public void SubmitBid(int playerIndex, int bidAmount)
    {
        if (_phase != GamePhase.Bidding || playerIndex != _currentPlayerIndex) return;

        // 叫分范围校验：0=不叫，1~3=叫分
        if (bidAmount < 0 || bidAmount > 3) return;

        _bidRound++;

        if (bidAmount > _currentBid)
        {
            _currentBid = bidAmount;
            _highestBidder = playerIndex;
            MessageChanged?.Invoke($"{_players[playerIndex].Name} 叫了 {bidAmount} 分");

            // 叫了 3 分（最高），立即分配地主
            if (bidAmount >= 3)
            {
                AssignLandlord(playerIndex);
                return;
            }
        }
        else
        {
            MessageChanged?.Invoke($"{_players[playerIndex].Name} 不叫");
        }

        // 所有玩家都叫过分后
        if (_bidRound >= 3)
        {
            if (_highestBidder.HasValue)
            {
                AssignLandlord(_highestBidder.Value);
            }
            else
            {
                MessageChanged?.Invoke("无人叫地主，重新发牌...");
                StartNewGame();
            }
            return;
        }

        _currentPlayerIndex = (_currentPlayerIndex + 1) % 3;
        TurnChanged?.Invoke(_currentPlayerIndex);
    }

    private void AssignLandlord(int landlordIndex)
    {
        _lastLandlordIndex = landlordIndex;
        LandlordIndex = landlordIndex;
        _players[landlordIndex].Role = PlayerRole.Landlord;
        _players[landlordIndex].Hand.AddRange(_kittyCards);
        CardHelper.SortHand(_players[landlordIndex].Hand);

        _multiplier = Math.Max(_currentBid, 1); // 快速模式下默认1倍
        CardsChanged?.Invoke(landlordIndex);

        MessageChanged?.Invoke($"{_players[landlordIndex].Name} 成为地主！({_multiplier}倍)");

        // 地主先出
        _currentPlayerIndex = landlordIndex;
        SetPhase(GamePhase.Playing);
        TurnChanged?.Invoke(_currentPlayerIndex);
    }

    // ========== 出牌 ==========
    public void SubmitPlay(int playerIndex, List<Card> cards)
    {
        if (_phase != GamePhase.Playing || playerIndex != _currentPlayerIndex) return;

        var combo = RulesEngine.ClassifyPlay(cards);
        if (!combo.IsValid) return;

        // 手牌所有权 + 重复牌校验
        var hand = _players[playerIndex].Hand;
        var requestedSet = new HashSet<(Suit, Rank)>(cards.Select(c => (c.Suit, c.Rank)));
        if (cards.Count != requestedSet.Count) return; // 重复牌
        var handSet = new HashSet<(Suit, Rank)>(hand.Select(c => (c.Suit, c.Rank)));
        if (!requestedSet.IsSubsetOf(handSet)) return; // 牌不在手中

        if (_lastPlayedCombo != null && _lastPlayedByIndex != playerIndex)
        {
            if (!RulesEngine.CanBeat(combo, _lastPlayedCombo))
                return;
        }

        if (combo.Type == CardComboType.Bomb || combo.Type == CardComboType.Rocket)
            _multiplier *= 2;

        // 从手牌中移除（requestedSet 已在上方校验过）
        hand.RemoveAll(c => requestedSet.Contains((c.Suit, c.Rank)));

        _lastPlayedCombo = combo;
        _lastPlayedByIndex = playerIndex;
        _consecutivePasses = 0;

        PlayerPlayed?.Invoke(playerIndex, combo);
        CardsChanged?.Invoke(playerIndex);

        if (hand.Count == 0)
        {
            SetPhase(GamePhase.GameOver);
            GameEnded?.Invoke(playerIndex, _multiplier);
            return;
        }

        MoveToNextPlayer();
    }

    public void SubmitPass(int playerIndex)
    {
        if (_phase != GamePhase.Playing || playerIndex != _currentPlayerIndex) return;

        if (_lastPlayedCombo == null || _lastPlayedByIndex == playerIndex) return;

        _consecutivePasses++;
        PlayerPlayed?.Invoke(playerIndex, null);

        if (_consecutivePasses >= 2)
        {
            _lastPlayedCombo = null;
            _lastPlayedByIndex = null;
            _consecutivePasses = 0;
        }

        MoveToNextPlayer();
    }

    public bool CanPlayerPlay(int playerIndex, List<Card> cards)
    {
        if (playerIndex != _currentPlayerIndex || _phase != GamePhase.Playing)
            return false;

        var combo = RulesEngine.ClassifyPlay(cards);
        if (!combo.IsValid) return false;

        if (_lastPlayedCombo == null || _lastPlayedByIndex == playerIndex)
            return true;

        return RulesEngine.CanBeat(combo, _lastPlayedCombo);
    }

    private void MoveToNextPlayer()
    {
        _currentPlayerIndex = (_currentPlayerIndex + 1) % 3;
        TurnChanged?.Invoke(_currentPlayerIndex);
    }

    private void SetPhase(GamePhase phase)
    {
        _phase = phase;
        PhaseChanged?.Invoke(phase);
    }
}
