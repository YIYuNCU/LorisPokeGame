// -----------------------------------------------------------------------
// GameViewModel.cs - 游戏主视图模型
// 连接 GameManager、AI、UI 的中枢
// -----------------------------------------------------------------------

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using LolitaPoker.Core.AI;
using LolitaPoker.Core.Assets;
using LolitaPoker.Core.Audio;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Game;
using LolitaPoker.Core.Models;
using LolitaPoker.Core.Network;

namespace LolitaPoker.Core.ViewModels;

/// <summary>
/// 游戏主视图模型 - 管理整个游戏的UI状态
/// </summary>
public class GameViewModel : ViewModelBase
{
    // ========== 内部组件 ==========
    private readonly GameManager _gameManager;
    private readonly SimpleAIPlayer _aiPlayer;
    private readonly DispatcherTimer _aiTimer;
    private readonly Random _rng = new();

    // ========== 洗牌/发牌动画 ==========
    private TaskCompletionSource? _shuffleTcs;
    private bool _isDealing;
    private CancellationTokenSource? _dealCts;
    private int? _pendingTurnPlayer;
    private readonly List<int> _pendingCardsChanged = new();

    // 服务器模式：动画期间暂存服务器消息
    private readonly List<(string type, string payload)> _serverPendingMessages = new();

    /// <summary>通知 View 播放洗牌动画</summary>
    public event Action? ShuffleRequested;

    /// <summary>通知 View 强制完成手牌区域布局（确保 CardControl 实例已创建）</summary>
    public event Action? HandLayoutRequested;

    /// <summary>洗牌动画完成后的回调（由 View 调用）</summary>
    public void OnShuffleComplete()
    {
        _shuffleTcs?.TrySetResult();
    }

    // ========== 选牌缓存 ==========
    // 持久保存玩家选中的牌，跨回合不丢失
    private readonly CardSelectionCache _selectionCache = new();

    // ========== 玩家 ==========
    public PlayerViewModel PlayerBottom { get; } = new() { SeatIndex = 0, IsHuman = true };
    public PlayerViewModel PlayerRight  { get; } = new() { SeatIndex = 1 };
    public PlayerViewModel PlayerLeft   { get; } = new() { SeatIndex = 2 };

    public PlayerViewModel[] AllPlayers => new[] { PlayerBottom, PlayerRight, PlayerLeft };

    /// <summary>
    /// 初始化三位玩家的昵称。必须在 StartNewGame 之前调用。
    /// </summary>
    /// <param name="bottomName">下方玩家昵称（人类）</param>
    /// <param name="rightName">右侧玩家昵称</param>
    /// <param name="leftName">左侧玩家昵称</param>
    public void InitializePlayers(string bottomName, string rightName, string leftName)
    {
        PlayerBottom.Name = string.IsNullOrWhiteSpace(bottomName) ? "玩家" : bottomName;
        PlayerRight.Name  = string.IsNullOrWhiteSpace(rightName)  ? "电脑A" : rightName;
        PlayerLeft.Name   = string.IsNullOrWhiteSpace(leftName)   ? "电脑B" : leftName;
    }

    // ========== 底牌 ==========
    private bool _landlordCardsVisible;

    public System.Collections.ObjectModel.ObservableCollection<CardViewModel> LandlordCards { get; } = new();

    public bool LandlordCardsVisible
    {
        get => _landlordCardsVisible;
        set => SetProperty(ref _landlordCardsVisible, value);
    }

    // ========== 状态 ==========
    private string _statusMessage = "点击「新游戏」开始";
    private string _hintNoPlayMessage = "";
    private GamePhase _currentPhase = GamePhase.Idle;
    private bool _isBidPanelVisible;
    private bool _isPlayPanelVisible;
    private bool _isNewGameVisible = true;
    private bool _isGameOver;
    private string _landlordLabel = "";

    // ========== 服务器模式房间号 ==========
    private string _roomCode = "";
    public string RoomCode
    {
        get => _roomCode;
        set => SetProperty(ref _roomCode, value);
    }

    private string _copyFeedback = "";
    public string CopyFeedback
    {
        get => _copyFeedback;
        set => SetProperty(ref _copyFeedback, value);
    }

    public bool IsServerMode => GameMode == GameMode.Server;

    // ========== 房间可见性 ==========
    private bool _isPublicRoom = true;
    private bool _isRoomCreator;
    public bool IsPublicRoom
    {
        get => _isPublicRoom;
        set
        {
            if (SetProperty(ref _isPublicRoom, value))
                OnPropertyChanged(nameof(RoomVisibilityText));
        }
    }

    /// <summary>当前玩家是否为房间创建者（仅创建者可修改可见性）</summary>
    public bool IsRoomCreator
    {
        get => _isRoomCreator;
        set
        {
            if (SetProperty(ref _isRoomCreator, value))
                OnPropertyChanged(nameof(IsRoomVisibilityToggleEnabled));
        }
    }

    /// <summary>可见性切换是否可用（仅创建者 + 游戏未开始时可用）</summary>
    public bool IsRoomVisibilityToggleEnabled =>
        IsRoomCreator && (CurrentPhase == GamePhase.Idle || CurrentPhase == GamePhase.GameOver);

    public string RoomVisibilityText => _isPublicRoom ? "🟢 公开" : "🔴 私密";

    /// <summary>
    /// 切换房间公开/私密状态
    /// </summary>
    public async void ToggleRoomVisibility()
    {
        if (!IsRoomVisibilityToggleEnabled) return;
        IsPublicRoom = !IsPublicRoom;
        if (_networkAdapter is WebSocketNetworkAdapter ws)
        {
            await ws.SetRoomVisibilityAsync(IsPublicRoom);
        }
    }

    // ========== 大厅玩家列表 ==========
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _lobbyPlayers = new();
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<string> LobbyPlayers { get; }

    private bool _isLobbyVisible;
    public bool IsLobbyVisible
    {
        get => _isLobbyVisible;
        set => SetProperty(ref _isLobbyVisible, value);
    }

    /// <summary>
    /// 从适配器同步大厅玩家列表
    /// </summary>
    internal void SyncLobbyFromAdapter()
    {
        if (_networkAdapter is not WebSocketNetworkAdapter ws) return;
        _lobbyPlayers.Clear();
        foreach (var p in ws.LobbyPlayers.OrderBy(p => p.Seat))
        {
            string readyTag = p.Ready ? " ✓ 已准备" : "";
            _lobbyPlayers.Add($"座位{p.Seat + 1}: {p.Name}{readyTag}");
        }
    }

    // ========== 断线投票 ==========
    private bool _isVoteVisible;
    public bool IsVoteVisible
    {
        get => _isVoteVisible;
        set => SetProperty(ref _isVoteVisible, value);
    }

    private string _voteMessage = "";
    public string VoteMessage
    {
        get => _voteMessage;
        set => SetProperty(ref _voteMessage, value);
    }

    private string _voteStatusMessage = "";
    public string VoteStatusMessage
    {
        get => _voteStatusMessage;
        set => SetProperty(ref _voteStatusMessage, value);
    }

    /// <summary>
    /// 将服务器座位号映射为本地视觉位置（自己永远在底部=0）
    /// </summary>
    private int ServerSeatToLocal(int serverSeat)
    {
        if (_networkAdapter is WebSocketNetworkAdapter ws && ws.AssignedSeat >= 0)
            return (serverSeat - ws.AssignedSeat + 3) % 3;
        return serverSeat;
    }

    private async void CopyRoomCode()
    {
        if (string.IsNullOrEmpty(RoomCode)) return;

        // 剪贴板可能被其他进程占用，重试几次
        for (int i = 0; i < 3; i++)
        {
            try
            {
                System.Windows.Clipboard.SetText(RoomCode);
                CopyFeedback = "已复制 ✓";

                // 2秒后清除提示
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, e) =>
                {
                    CopyFeedback = "";
                    timer.Stop();
                };
                timer.Start();
                return;
            }
            catch
            {
                await Task.Delay(100);
            }
        }

        CopyFeedback = "复制失败";
    }

    // ========== 返回菜单可见性 ==========
    // 人机模式：始终可用（退出判负）
    // 联机模式：仅 Idle 阶段可用
    private bool _isBackToMenuVisible = true;
    public bool IsBackToMenuVisible
    {
        get => _isBackToMenuVisible;
        set => SetProperty(ref _isBackToMenuVisible, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>出牌阶段的提示文本（仅点提示且无牌可出时显示）</summary>
    public string HintNoPlayMessage
    {
        get => _hintNoPlayMessage;
        set => SetProperty(ref _hintNoPlayMessage, value);
    }

    public GamePhase CurrentPhase
    {
        get => _currentPhase;
        set
        {
            if (SetProperty(ref _currentPhase, value))
            {
                // 人机模式始终允许退出；联机模式仅 Idle 阶段可退出
                IsBackToMenuVisible = GameMode == GameMode.HumanVsAI || value == GamePhase.Idle;
                OnPropertyChanged(nameof(IsRoomVisibilityToggleEnabled));
            }
        }
    }

    public bool IsBidPanelVisible
    {
        get => _isBidPanelVisible;
        set => SetProperty(ref _isBidPanelVisible, value);
    }

    private bool _canBid1 = true;
    private bool _canBid2 = true;
    private bool _canBid3 = true;

    public bool CanBid1 { get => _canBid1; set => SetProperty(ref _canBid1, value); }
    public bool CanBid2 { get => _canBid2; set => SetProperty(ref _canBid2, value); }
    public bool CanBid3 { get => _canBid3; set => SetProperty(ref _canBid3, value); }

    public bool IsPlayPanelVisible
    {
        get => _isPlayPanelVisible;
        set => SetProperty(ref _isPlayPanelVisible, value);
    }

    public bool IsNewGameVisible
    {
        get => _isNewGameVisible;
        set => SetProperty(ref _isNewGameVisible, value);
    }

    public bool IsGameOver
    {
        get => _isGameOver;
        set => SetProperty(ref _isGameOver, value);
    }

    public string LandlordLabel
    {
        get => _landlordLabel;
        set => SetProperty(ref _landlordLabel, value);
    }

    // ========== 出牌倒计时 ==========
    private int _turnCountdown;
    private DispatcherTimer? _countdownTimer;
    private int _turnTimeoutSeconds = 30;

    public int TurnTimeoutSeconds
    {
        get => _turnTimeoutSeconds;
        set => SetProperty(ref _turnTimeoutSeconds, value);
    }

    public string TurnCountdownText => _turnCountdown > 0 ? $"{_turnCountdown}s" : "";

    public bool IsCountdownVisible => IsPlayPanelVisible && _turnCountdown > 0 && GameMode == GameMode.Server;

    private void StartCountdown()
    {
        if (GameMode != GameMode.Server) return;
        _turnCountdown = _turnTimeoutSeconds;
        OnPropertyChanged(nameof(TurnCountdownText));
        OnPropertyChanged(nameof(IsCountdownVisible));

        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            _turnCountdown--;
            OnPropertyChanged(nameof(TurnCountdownText));
            if (_turnCountdown <= 0)
            {
                _countdownTimer.Stop();
                _turnCountdown = 0;
                OnPropertyChanged(nameof(IsCountdownVisible));
                // 超时自动不出
                if (IsPlayPanelVisible)
                    PassPlay();
            }
        };
        _countdownTimer.Start();
    }

    private void StopCountdown()
    {
        _countdownTimer?.Stop();
        _turnCountdown = 0;
        OnPropertyChanged(nameof(TurnCountdownText));
        OnPropertyChanged(nameof(IsCountdownVisible));
    }

    // ========== 提示系统 ==========
    private List<CardCombo> _hints = new();
    private int _hintIndex;

    // ========== 音频服务 ==========
    private readonly ITtsService _ttsService;
    private readonly IBgmService _bgmService;
    private TaskCompletionSource? _ttsReadyTcs;

    // ========== 服务器模式：跟踪上一手出牌 ==========
    private CardCombo? _lastServerCombo;
    private int _serverConsecutivePasses;

    // ========== 命令 ==========
    public ICommand NewGameCommand { get; }
    public ICommand ConfirmGameOverCommand { get; }
    public ICommand Bid1Command { get; }
    public ICommand Bid2Command { get; }
    public ICommand Bid3Command { get; }
    public ICommand PassBidCommand { get; }
    public ICommand PlayCardsCommand { get; }
    public ICommand PassPlayCommand { get; }
    public ICommand HintCommand { get; }
    public ICommand BackToMenuCommand { get; }
    public ICommand CopyRoomCodeCommand { get; }
    public ICommand ToggleReadyCommand { get; }
    public ICommand VoteEndCommand { get; }
    public ICommand VoteContinueCommand { get; }

    // ========== 服务器模式准备状态 ==========
    private bool _isReady;
    public bool IsReady
    {
        get => _isReady;
        set
        {
            if (SetProperty(ref _isReady, value))
                OnPropertyChanged(nameof(ReadyButtonText));
        }
    }

    public string ReadyButtonText
    {
        get
        {
            if (GameMode == GameMode.Server)
                return IsReady ? "取消准备" : "准备游戏";
            return "新游戏";
        }
    }

    // ========== 联机支持 ==========
    private INetworkAdapter? _networkAdapter;
    private NetworkGameManager? _networkGameManager;

    /// <summary>当前游戏模式</summary>
    public GameMode GameMode { get; set; } = GameMode.HumanVsAI;

    /// <summary>是否处于联机模式</summary>
    public bool IsNetworkMode => _networkAdapter != null;

    /// <summary>返回菜单的回调（由 MainViewModel 设置）</summary>
    public Action? RequestNavigateToMenu { get; set; }

    public GameViewModel(ITtsService? ttsService = null, IBgmService? bgmService = null)
    {
        _ttsService = ttsService ?? NullTtsService.Instance;
        _bgmService = bgmService ?? NullBgmService.Instance;

        // 默认昵称
        InitializePlayers("玩家", "电脑A", "电脑B");

        // 大厅列表
        LobbyPlayers = new System.Collections.ObjectModel.ReadOnlyObservableCollection<string>(_lobbyPlayers);

        _gameManager = new GameManager();
        _aiPlayer = new SimpleAIPlayer();

        // AI 出牌延时定时器
        _aiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _aiTimer.Tick += OnAiTimerTick;

        // 订阅游戏事件
        _gameManager.PhaseChanged += OnPhaseChanged;
        _gameManager.TurnChanged += OnTurnChanged;
        _gameManager.PlayerPlayed += OnPlayerPlayed;
        _gameManager.CardsChanged += OnCardsChanged;
        _gameManager.GameEnded += OnGameEnded;
        _gameManager.MessageChanged += msg => StatusMessage = msg;

        // 订阅选牌缓存变化事件
        _selectionCache.SelectionChanged += OnSelectionChanged;

        // 订阅卡牌选中状态变化事件
        CardViewModel.SelectionStateChanged += OnCardSelectionChanged;

        // 绑定命令
        NewGameCommand = new RelayCommand(_ => StartNewGame());
        ConfirmGameOverCommand = new RelayCommand(_ => ConfirmGameOver());
        Bid1Command = new RelayCommand(_ => SubmitBid(1));
        Bid2Command = new RelayCommand(_ => SubmitBid(2));
        Bid3Command = new RelayCommand(_ => SubmitBid(3));
        PassBidCommand = new RelayCommand(_ => SubmitBid(0));
        PlayCardsCommand = new RelayCommand(_ => PlaySelectedCards(), _ => CanPlaySelectedCards());
        PassPlayCommand = new RelayCommand(_ => PassPlay(), _ => IsPlayPanelVisible);
        HintCommand = new RelayCommand(_ => ShowHint());
        BackToMenuCommand = new RelayCommand(_ => ExitToMenu());
        CopyRoomCodeCommand = new RelayCommand(_ => CopyRoomCode());
        ToggleReadyCommand = new RelayCommand(_ => ToggleReady());
        VoteEndCommand = new RelayCommand(_ => _ = SubmitVote("end"));
        VoteContinueCommand = new RelayCommand(_ => _ = SubmitVote("continue"));
    }

    // ========== 初始化联机模式 ==========

    /// <summary>
    /// 初始化联机模式，替换本地AI为网络玩家
    /// </summary>
    public void InitializeNetworkMode(INetworkAdapter networkAdapter)
    {
        _networkAdapter = networkAdapter;
        _networkGameManager = new NetworkGameManager(_gameManager, networkAdapter);

        // 联机模式下订阅网络事件
        _networkGameManager.GameStateReceived += OnNetworkGameStateReceived;
        _networkGameManager.OpponentActionReceived += OnNetworkOpponentAction;
        _networkGameManager.PlayerJoined += OnNetworkPlayerJoined;
        _networkGameManager.PlayerLeft += OnNetworkPlayerLeft;

        // 服务器模式：订阅服务器消息
        _networkGameManager.ServerMessageReceived += OnServerMessageReceived;
    }

    // ========== 新游戏 ==========

    private void StartNewGame()
    {
        // 重置 TTS 状态
        _ttsReadyTcs?.TrySetResult();
        _ttsReadyTcs = null;

        // 取消进行中的发牌动画，清理残留状态
        _dealCts?.Cancel();
        _dealCts?.Dispose();
        _dealCts = null;
        _isDealing = false;
        _shuffleTcs?.TrySetResult(); // 确保不挂起
        _shuffleTcs = null;
        _serverPendingMessages.Clear();

        // 重置UI
        PlayerBottom.Hand.Clear();
        PlayerRight.Hand.Clear();
        PlayerLeft.Hand.Clear();
        PlayerBottom.PlayedCards.Clear();
        PlayerRight.PlayedCards.Clear();
        PlayerLeft.PlayedCards.Clear();
        LandlordCards.Clear();
        LandlordCardsVisible = false;
        LandlordLabel = "";

        // 清除选中缓存
        _selectionCache.Clear();

        foreach (var p in AllPlayers)
        {
            p.Role = PlayerRole.Farmer;
            p.LastAction = "";
            p.IsCurrentTurn = false;
            p.IsThinking = false;
        }

        IsNewGameVisible = false;
        IsGameOver = false;
        IsReady = false;

        if (IsNetworkMode && _networkGameManager != null)
        {
            if (GameMode == GameMode.Server)
            {
                // 服务器模式：显示准备按钮和大厅
                SyncLobbyFromAdapter();
                IsLobbyVisible = true;
                IsNewGameVisible = true;
                StatusMessage = "点击「准备游戏」开始";
                return;
            }
            else
            {
                // P2P模式：通知网络开始游戏
                _networkGameManager.RequestNewGame();
            }
        }
        else
        {
            // 单机模式：叫分选地主
            _gameManager.StartNewGame();
        }
    }

    // ========== 服务器模式准备/取消准备 ==========

    private void ToggleReady()
    {
        // 非服务器模式：直接开始新游戏
        if (GameMode != GameMode.Server)
        {
            StartNewGame();
            return;
        }

        if (_networkAdapter == null) return;

        if (!IsReady)
        {
            // 准备
            IsReady = true;
            // 立即更新适配器中本地玩家的准备状态（不等服务器回传）
            if (_networkAdapter is WebSocketNetworkAdapter ws)
            {
                var me = ws.LobbyPlayers.FirstOrDefault(p => p.Seat == ws.AssignedSeat);
                if (me != null) me.Ready = true;
            }
            IsLobbyVisible = true; // 确保大厅可见（游戏结束后可能被隐藏）
            SyncLobbyFromAdapter(); // 立即更新自己的状态
            _ = _networkAdapter.SendMessageAsync(new NetworkMessage { Type = "ready", Payload = "{}" });
            StatusMessage = "已准备，等待其他玩家...";
        }
        else
        {
            // 取消准备
            IsReady = false;
            if (_networkAdapter is WebSocketNetworkAdapter ws2)
            {
                var me = ws2.LobbyPlayers.FirstOrDefault(p => p.Seat == ws2.AssignedSeat);
                if (me != null) me.Ready = false;
            }
            SyncLobbyFromAdapter();
            _ = _networkAdapter.SendMessageAsync(new NetworkMessage { Type = "cancel_ready", Payload = "{}" });
            StatusMessage = "已取消准备";
        }
    }

    // ========== 叫地主 ==========

    private void SubmitBid(int amount)
    {
        IsBidPanelVisible = false;

        if (GameMode == GameMode.Server && _networkAdapter != null)
        {
            // 服务器模式：发送给服务器
            _ = _networkAdapter.SendMessageAsync(new NetworkMessage
            {
                Type = "bid",
                Payload = System.Text.Json.JsonSerializer.Serialize(new { amount })
            });
        }
        else
        {
            _gameManager.SubmitBid(0, amount);
            if (IsNetworkMode && _networkGameManager != null)
                _networkGameManager.SendBid(amount);
        }
    }

    // ========== 出牌 ==========

    private void PlaySelectedCards()
    {
        HintNoPlayMessage = "";
        StopCountdown();
        var selected = _selectionCache.GetSelectedCardModels(PlayerBottom.Hand);

        if (selected.Count == 0) return;

        if (GameMode == GameMode.Server && _networkAdapter != null)
        {
            // 服务器模式：发送给服务器
            var cardData = selected.Select(c => new { suit = (int)c.Suit, rank = (int)c.Rank }).ToArray();
            _ = _networkAdapter.SendMessageAsync(new NetworkMessage
            {
                Type = "play",
                Payload = System.Text.Json.JsonSerializer.Serialize(new { cards = cardData })
            });
        }
        else
        {
            _gameManager.SubmitPlay(0, selected);
            if (IsNetworkMode && _networkGameManager != null)
                _networkGameManager.SendPlay(selected);
        }

        // 出牌成功后清除选中缓存
        _selectionCache.Clear();
    }

    private bool CanPlaySelectedCards()
    {
        if (GameMode == GameMode.Server)
        {
            if (!IsPlayPanelVisible) return false;
            var selected = _selectionCache.GetSelectedCardModels(PlayerBottom.Hand);
            if (selected.Count == 0) return false;
            // 本地预校验牌型合法性
            var combo = RulesEngine.ClassifyPlay(selected);
            if (!combo.IsValid) return false;
            // 有上手牌时，检查能否压过
            if (_lastServerCombo != null)
                return RulesEngine.CanBeat(combo, _lastServerCombo);
            return true;
        }

        if (_gameManager.CurrentPlayerIndex != 0) return false;
        if (_gameManager.Phase != GamePhase.Playing) return false;

        var sel = _selectionCache.GetSelectedCardModels(PlayerBottom.Hand);

        if (sel.Count == 0) return false;

        var c = RulesEngine.ClassifyPlay(sel);
        return c.IsValid && _gameManager.CanPlayerPlay(0, sel);
    }

    private async void PassPlay()
    {
        HintNoPlayMessage = "";
        StopCountdown();
        if (GameMode == GameMode.Server && _networkAdapter != null)
        {
            // 服务器模式：发送给服务器，等服务器确认后再隐藏面板
            try
            {
                await _networkAdapter.SendMessageAsync(new NetworkMessage
                {
                    Type = "pass",
                    Payload = "{}"
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"跳过失败: {ex.Message}";
            }
            // 面板由服务器响应（player_passed/turn_change/error）控制隐藏
        }
        else
        {
            _gameManager.SubmitPass(0);
            if (IsNetworkMode && _networkGameManager != null)
                _networkGameManager.SendPass();
        }
    }

    // ========== 选牌缓存事件处理 ==========

    private void OnSelectionChanged()
    {
        // 当选牌状态变化时，刷新命令状态
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnCardSelectionChanged(CardViewModel cardVm, bool isSelected)
    {
        // 将卡牌选中状态同步到缓存
        _selectionCache.SetSelection(cardVm.Model.Suit, cardVm.Model.Rank, isSelected);
    }

    // ========== 提示 ==========

    private void ShowHint()
    {
        if (_hints.Count == 0)
        {
            if (GameMode == GameMode.Server)
            {
                // 服务器模式：从 UI 手牌构建提示，使用跟踪的上手牌型
                var hand = PlayerBottom.Hand.Select(vm => vm.Model).ToList();
                _hints = CardComboFinder.FindAllPlayableCombos(hand, _lastServerCombo).ToList();
            }
            else
            {
                _hints = CardComboFinder.FindAllPlayableCombos(
                    _gameManager.GetPlayerHand(0),
                    _gameManager.LastPlayedCombo
                ).ToList();
            }
            _hintIndex = 0;
        }

        if (_hints.Count == 0)
        {
            HintNoPlayMessage = "没有可以出的牌，请选择不出";
            return;
        }

        // 有可出的牌，清除无牌提示
        HintNoPlayMessage = "";

        // 清除所有选中状态
        _selectionCache.Clear();

        // 选中提示的牌
        var hint = _hints[_hintIndex % _hints.Count];
        foreach (var card in hint.Cards)
        {
            _selectionCache.SetSelection(card.Suit, card.Rank, true);
        }

        // 将缓存的选中状态应用到手牌UI
        _selectionCache.ApplyToHand(PlayerBottom.Hand);

        _hintIndex++;
    }

    // ========== 游戏事件处理 ==========

    private void OnPhaseChanged(GamePhase phase)
    {
        CurrentPhase = phase;

        // 发牌动画期间不触发UI变化，动画完成后统一处理
        if (_isDealing) return;

        IsBidPanelVisible = false;
        IsPlayPanelVisible = false;

        switch (phase)
        {
            case GamePhase.Dealing:
                StatusMessage = "";
                _ = AnimatedDealAsync(); // 异步启动，不阻塞UI
                break;
            case GamePhase.Bidding:
                StatusMessage = "叫地主阶段";
                break;
            case GamePhase.Playing:
                StatusMessage = "出牌阶段";
                break;
            case GamePhase.GameOver:
                _aiTimer.Stop();
                break;
        }
    }

    private void OnTurnChanged(int playerIndex)
    {
        // 发牌动画期间暂存，动画完成后统一处理
        if (_isDealing)
        {
            _pendingTurnPlayer = playerIndex;
            return;
        }

        ProcessTurnChanged(playerIndex);
    }

    private void ProcessTurnChanged(int playerIndex)
    {
        // 更新回合指示器
        foreach (var p in AllPlayers)
            p.IsCurrentTurn = p.SeatIndex == playerIndex;

        // 清空上次出牌显示
        AllPlayers[playerIndex].PlayedCards.Clear();
        AllPlayers[playerIndex].LastAction = "";
        AllPlayers[playerIndex].IsThinking = false;

        if (_gameManager.Phase == GamePhase.Bidding)
        {
            if (playerIndex == 0)
            {
                // 人类玩家叫分
                int currentBid = _gameManager.CurrentBid;
                CanBid1 = currentBid < 1;
                CanBid2 = currentBid < 2;
                CanBid3 = currentBid < 3;
                IsBidPanelVisible = true;
                StatusMessage = currentBid > 0
                    ? $"请叫分 (最低 {currentBid + 1} 分，或不叫)"
                    : "请叫分 (1-3 分，或不叫)";
            }
            else
            {
                // AI 叫分
                ScheduleAiAction(playerIndex, isBidding: true);
            }
        }
        else if (_gameManager.Phase == GamePhase.Playing)
        {
            if (playerIndex == 0)
            {
                // 人类玩家出牌
                IsPlayPanelVisible = true;
                _hints = new List<CardCombo>();
                _hintIndex = 0;
                HintNoPlayMessage = "";
                HintNoPlayMessage = "";

                // 将缓存的选中状态应用到 Hand
                _selectionCache.ApplyToHand(PlayerBottom.Hand);

                // 刷新命令状态，更新出牌按钮
                CommandManager.InvalidateRequerySuggested();

                bool isLeading = _gameManager.LastPlayedCombo == null;
                StatusMessage = isLeading ? "你的回合，请出牌" : "请出牌或选择不出";
            }
            else
            {
                // AI 出牌
                ScheduleAiAction(playerIndex, isBidding: false);
            }
        }
    }

    private void OnPlayerPlayed(int playerIndex, CardCombo? combo)
    {
        var player = AllPlayers[playerIndex];

        if (combo == null)
        {
            // 不出
            player.LastAction = "不出";
            player.PlayedCards.Clear();
        }
        else
        {
            // 显示出的牌
            player.LastAction = "";
            player.PlayedCards.Clear();
            foreach (var card in combo.Cards)
            {
                player.PlayedCards.Add(new CardViewModel(card, true) { IsPlayable = false });
            }

            // TTS 播报出牌内容（仅本地模式下触发，服务器模式由客户端自行播报）
            if (GameMode != GameMode.Server && _ttsService.IsAvailable)
            {
                _ttsReadyTcs = new TaskCompletionSource();
                var ttsText = combo.GetDescription();
                _ttsService.SpeakAsync(ttsText).ContinueWith(_ =>
                {
                    _ttsReadyTcs?.TrySetResult();
                }, TaskScheduler.Default);
            }
        }
    }

    private void OnCardsChanged(int playerIndex)
    {
        // 发牌动画期间暂存，动画完成后统一刷新
        if (_isDealing)
        {
            if (!_pendingCardsChanged.Contains(playerIndex))
                _pendingCardsChanged.Add(playerIndex);
            return;
        }

        RefreshPlayerHand(playerIndex);
    }

    private void OnGameEnded(int? winnerIndex, int multiplier)
    {
        _aiTimer.Stop();

        string winnerName = winnerIndex.HasValue ? AllPlayers[winnerIndex.Value].Name : "无";
        bool landlordWon = winnerIndex.HasValue &&
            AllPlayers[winnerIndex.Value].Role == PlayerRole.Landlord;
        bool playerIsLandlord = PlayerBottom.Role == PlayerRole.Landlord;

        string msg;
        if (winnerIndex == 0)
        {
            msg = $"🎉 你赢了！（{multiplier}倍）";
        }
        else if (landlordWon)
        {
            // 地主获胜
            if (playerIsLandlord)
                msg = $"🎉 你赢了！（{multiplier}倍）";
            else
                msg = $"地主 {winnerName} 获胜！你输了（{multiplier}倍）";
        }
        else
        {
            // 农民获胜
            if (playerIsLandlord)
                msg = $"农民获胜！你输了（{multiplier}倍）";
            else
                msg = $"🎉 农民获胜！你赢了（{multiplier}倍）";
        }

        StatusMessage = msg;
        IsPlayPanelVisible = false;
        IsBidPanelVisible = false;
        IsGameOver = true;

        // 展示底牌
        LandlordCardsVisible = true;
    }

    private void ConfirmGameOver()
    {
        // 重置出牌区域
        foreach (var p in AllPlayers)
        {
            p.PlayedCards.Clear();
            p.LastAction = "";
        }

        IsGameOver = false;
        CurrentPhase = GamePhase.Idle; // 回到 Idle，显示返回按钮

        if (GameMode == GameMode.Server)
        {
            // 服务器模式：显示大厅（含玩家列表和准备状态）+ 准备按钮
            SyncLobbyFromAdapter();
            IsLobbyVisible = true;
            IsNewGameVisible = true;
            StatusMessage = "点击「准备游戏」再来一局";
        }
        else
        {
            IsNewGameVisible = true;
        }
    }

    /// <summary>
    /// 退出到主页。游戏中退出视为判负。
    /// </summary>
    private void ExitToMenu()
    {
        // 游戏进行中退出 → 判负
        if (CurrentPhase == GamePhase.Dealing
            || CurrentPhase == GamePhase.Bidding
            || CurrentPhase == GamePhase.Playing)
        {
            _aiTimer.Stop();

            bool playerIsLandlord = PlayerBottom.Role == PlayerRole.Landlord;
            int multi = _gameManager.Multiplier;

            StatusMessage = playerIsLandlord
                ? $"你退出了游戏，判负（{multi}倍）"
                : $"你退出了游戏，判负（{multi}倍）";

            IsPlayPanelVisible = false;
            IsBidPanelVisible = false;
            CurrentPhase = GamePhase.GameOver;
            IsGameOver = false; // 不显示确认按钮，直接退出
            IsNewGameVisible = false;
            LandlordCardsVisible = true;
        }

        RequestNavigateToMenu?.Invoke();
    }

    // ========== AI 处理 ==========

    private void ScheduleAiAction(int playerIndex, bool isBidding)
    {
        AllPlayers[playerIndex].IsThinking = true;
        StatusMessage = $"{AllPlayers[playerIndex].Name} 思考中...";

        // 使用延时让AI看起来在"思考"
        int delay = 600 + _rng.Next(600);
        _aiTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _aiTimer.Tag = (playerIndex, isBidding);
        _aiTimer.Start();
    }

    private void OnAiTimerTick(object? sender, EventArgs e)
    {
        _aiTimer.Stop();

        // TTS 尚未播放完毕时，等待 100ms 后重试
        if (_ttsReadyTcs != null && !_ttsReadyTcs.Task.IsCompleted)
        {
            _aiTimer.Interval = TimeSpan.FromMilliseconds(100);
            _aiTimer.Start();
            return;
        }

        var (playerIndex, isBidding) = ((int, bool))_aiTimer.Tag!;
        AllPlayers[playerIndex].IsThinking = false;

        if (isBidding)
        {
            var hand = _gameManager.GetPlayerHand(playerIndex);
            int bid = _aiPlayer.DecideBid(hand, _gameManager.CurrentBid);
            _gameManager.SubmitBid(playerIndex, bid);
        }
        else
        {
            var hand = _gameManager.GetPlayerHand(playerIndex);
            bool isLandlord = _gameManager.LandlordIndex == playerIndex;
            var result = _aiPlayer.DecidePlay(
                hand,
                _gameManager.LastPlayedCombo,
                _gameManager.LastPlayedCombo == null,
                isLandlord,
                _gameManager.LastPlayedByIndex,
                playerIndex,
                _gameManager.LandlordIndex
            );

            if (result.pass || result.cards == null)
            {
                _gameManager.SubmitPass(playerIndex);
            }
            else
            {
                _gameManager.SubmitPlay(playerIndex, result.cards);
            }
        }
    }

    // ========== 数据同步 ==========

    /// <summary>
    /// 同步发牌（无动画降级方案 / 联机模式）
    /// </summary>
    private void DealCards()
    {
        PlayerBottom.Hand.Clear();
        PlayerRight.Hand.Clear();
        PlayerLeft.Hand.Clear();

        for (int i = 0; i < 3; i++)
        {
            var hand = _gameManager.GetPlayerHand(i);
            var vm = AllPlayers[i];

            foreach (var card in hand)
            {
                bool faceUp = i == 0;
                vm.Hand.Add(new CardViewModel(card, faceUp));
            }

            vm.CardCount = hand.Count;
        }

        LandlordCards.Clear();
        foreach (var card in _gameManager.KittyCards)
            LandlordCards.Add(new CardViewModel(card, false)); // 底牌倒扣
        LandlordCardsVisible = true; // 叫分阶段显示背面
    }

    /// <summary>
    /// 带动画的发牌流程：
    /// 洗牌 → 逐张发牌(背面) → 聚拢 → 翻牌 → 铺开
    /// </summary>
    private async Task AnimatedDealAsync()
    {
        _isDealing = true;
        _dealCts = new CancellationTokenSource();
        var ct = _dealCts.Token;

        try
        {
            // 清空UI
            PlayerBottom.Hand.Clear();
            PlayerRight.Hand.Clear();
            PlayerLeft.Hand.Clear();
            LandlordCards.Clear();

            // ── 阶段1: 洗牌动画 ──
            _shuffleTcs = new TaskCompletionSource();
            ShuffleRequested?.Invoke();
            await Task.Delay(1800, ct);
            ct.ThrowIfCancellationRequested();

            // ── 阶段2: 逐张发牌（全部背面） ──
            StatusMessage = "发牌中...";

            var handVms = new List<CardViewModel>[3];
            for (int i = 0; i < 3; i++)
            {
                var hand = _gameManager.GetPlayerHand(i);
                handVms[i] = hand.Select(card =>
                    new CardViewModel(card, faceUp: false)).ToList();
            }

            // 轮转发牌：每轮3张（3位玩家各1张），间隔60ms
            for (int cardIdx = 0; cardIdx < 17; cardIdx++)
            {
                for (int playerIdx = 0; playerIdx < 3; playerIdx++)
                    AllPlayers[playerIdx].Hand.Add(handVms[playerIdx][cardIdx]);

                if (cardIdx < 16)
                    await Task.Delay(60, ct);
            }

            // 更新AI玩家的卡牌数
            for (int i = 1; i < 3; i++)
                AllPlayers[i].CardCount = 17;

            // 设置底牌（背面，叫分阶段可见）
            foreach (var card in _gameManager.KittyCards)
                LandlordCards.Add(new CardViewModel(card, false));
            LandlordCardsVisible = true;

            // ── 阶段3: 聚拢玩家手牌 ──
            StatusMessage = "理牌中...";
            var playerHand = PlayerBottom.Hand;
            foreach (var cardVm in playerHand)
            {
                cardVm.GatherOffset = -20;
                cardVm.AnimationState = CardAnimation.Gathering;
            }
            await Task.Delay(350, ct);

            // ── 阶段4: 依次翻牌 ──
            for (int i = 0; i < playerHand.Count; i++)
            {
                var cardVm = playerHand[i];
                cardVm.IsFaceUp = true;
                cardVm.AnimationState = CardAnimation.Revealing;
                // OnRevealCompleted 回调中会自动设为 Revealed → 触发铺开动画
                await Task.Delay(20, ct);
            }

            // 等待最后一张翻牌+铺开动画完成
            await Task.Delay(600, ct);

            // ── 阶段5: 清理 ──
            PlayerBottom.CardCount = 17;

            // 处理动画期间积压的事件
            FlushPendingEvents();
        }
        catch (OperationCanceledException)
        {
            // 动画被取消（新游戏开始），静默处理
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AnimatedDealAsync error: {ex.Message}");
            DealCards();
            _isDealing = false;
            FlushPendingEvents();
        }
        finally
        {
            _isDealing = false;
            _dealCts?.Dispose();
            _dealCts = null;
        }
    }

    /// <summary>
    /// 处理动画期间积压的 GameManager 事件
    /// </summary>
    private void FlushPendingEvents()
    {
        // 刷新积压的手牌变更
        foreach (var idx in _pendingCardsChanged)
            RefreshPlayerHand(idx);
        _pendingCardsChanged.Clear();

        // 处理延迟的回合切换
        if (_pendingTurnPlayer.HasValue)
        {
            int player = _pendingTurnPlayer.Value;
            _pendingTurnPlayer = null;

            // 传统模式：显示叫地主UI
            if (_gameManager.Phase == GamePhase.Bidding)
            {
                IsBidPanelVisible = false;
                IsPlayPanelVisible = false;
                StatusMessage = "叫地主阶段";
            }

            ProcessTurnChanged(player);
        }
    }

    private void RefreshPlayerHand(int playerIndex)
    {
        var vm = AllPlayers[playerIndex];
        var hand = _gameManager.GetPlayerHand(playerIndex);

        vm.CardCount = hand.Count;

        // 同步角色（叫地主完成后）
        if (_gameManager.LandlordIndex.HasValue)
        {
            for (int i = 0; i < 3; i++)
            {
                AllPlayers[i].Role = i == _gameManager.LandlordIndex.Value
                    ? PlayerRole.Landlord : PlayerRole.Farmer;
            }
            var landlord = AllPlayers[_gameManager.LandlordIndex.Value];
            LandlordLabel = $"👑 {landlord.Name} 是地主";
            LandlordCardsVisible = true;

            // 底牌翻为正面
            foreach (var kitty in LandlordCards)
                kitty.IsFaceUp = true;
        }

        if (playerIndex == 0)
        {
            // 人类玩家：重建手牌，保持选中状态
            RebuildPlayerHand(vm, hand);
        }
        else
        {
            // AI 玩家：只更新数量
            vm.CardCount = hand.Count;
        }
    }

    /// <summary>
    /// 从当前手牌UI同步选中状态到缓存
    /// </summary>
    private void SyncCachedSelections()
    {
        _selectionCache.SyncFromHand(PlayerBottom.Hand);
    }

    /// <summary>
    /// 保存当前手牌的选中状态到缓存（在清空手牌前调用）
    /// </summary>
    private void SaveSelectionsBeforeRebuild()
    {
        _selectionCache.SyncFromHand(PlayerBottom.Hand);
    }

    /// <summary>
    /// 重建玩家手牌UI，使用持久缓存保持选中状态
    /// </summary>
    private void RebuildPlayerHand(PlayerViewModel vm, IReadOnlyList<Card> hand)
    {
        if (vm.SeatIndex == 0)
        {
            // 在清空 Hand 前，保存所有选中状态到缓存
            SaveSelectionsBeforeRebuild();
        }

        vm.Hand.Clear();
        foreach (var card in hand)
        {
            bool isSelected = vm.SeatIndex == 0 && _selectionCache.IsSelected(card.Suit, card.Rank);
            var cardVm = new CardViewModel(card, true) { IsSelected = isSelected };
            vm.Hand.Add(cardVm);
        }
    }

    // ========== 联机事件处理 ==========

    private void OnNetworkGameStateReceived(string jsonData)
    {
        // P2P 模式的状态同步（待实现）
    }

    private void OnNetworkOpponentAction(string playerId, string actionType, string jsonData)
    {
        // P2P 模式的对手操作处理（待实现）
    }

    private void OnNetworkPlayerJoined(string playerId, string playerName)
    {
        // payload 格式：{"playerId":"xxx","playerName":"Bob","seat":1}
        // 注意：此事件在 WebSocket 接收线程触发，必须调度到 UI 线程
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(playerName);
            var root = doc.RootElement;
            var name = root.GetProperty("playerName").GetString() ?? "玩家";
            int serverSeat = root.GetProperty("seat").GetInt32();

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                int localSeat = ServerSeatToLocal(serverSeat);
                if (localSeat >= 0 && localSeat < 3)
                    AllPlayers[localSeat].Name = name;
                StatusMessage = $"{name} 加入了游戏";
                SyncLobbyFromAdapter();
                IsLobbyVisible = true;
            });
        }
        catch
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StatusMessage = "新玩家加入了游戏";
            });
        }
    }

    private void OnNetworkPlayerLeft(string playerId)
    {
        // 此事件在 WebSocket 接收线程触发，调度到 UI 线程
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            StatusMessage = "对手已离开游戏";
            SyncLobbyFromAdapter();
        });
    }

    // ========== 服务器消息处理 ==========

    /// <summary>
    /// 处理来自 FastAPI 服务器的消息
    /// </summary>
    private void OnServerMessageReceived(string type, string payloadJson)
    {
        // 发牌动画期间暂存消息，动画完成后统一处理（包括 GameStart，防止重发牌时多次触发动画）
        if (_isDealing)
        {
            _serverPendingMessages.Add((type, payloadJson));
            return;
        }

        // 所有 HandleServer* 方法都操作 ObservableCollection，必须调度到UI线程
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DispatchServerMessage(type, payloadJson);
        });
    }

    private void DispatchServerMessage(string type, string payloadJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            switch (type)
            {
                case "GameStart":
                    HandleServerGameStart(root);
                    break;
                case "PlayerReady":
                    HandleServerPlayerReady(root);
                    break;
                case "TurnChange":
                    HandleServerTurnChange(root);
                    break;
                case "BidUpdate":
                    HandleServerBidUpdate(root);
                    break;
                case "LandlordAssigned":
                    HandleServerLandlordAssigned(root);
                    break;
                case "CardsPlayed":
                    HandleServerCardsPlayed(root);
                    break;
                case "PlayerPassed":
                    HandleServerPlayerPassed(root);
                    break;
                case "GameOver":
                    HandleServerGameOver(root);
                    break;
                case "GameRestart":
                    HandleServerGameRestart(root);
                    break;
                case "Reconnected":
                    HandleServerReconnected(root);
                    break;
                case "VoteStart":
                    HandleServerVoteStart(root);
                    break;
                case "VoteUpdate":
                    HandleServerVoteUpdate(root);
                    break;
                case "ReconnectWaiting":
                    HandleServerReconnectWaiting(root);
                    break;
                case "PlayerReconnected":
                    HandleServerPlayerReconnected(root);
                    break;
                case "GameEnded":
                    HandleServerGameEnded(root);
                    break;
                case "Error":
                    var msg = root.GetProperty("message").GetString() ?? "未知错误";
                    StatusMessage = $"错误: {msg}";
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[GameVM] 消息处理异常 type={type}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void FlushServerPendingEvents()
    {
        var pending = _serverPendingMessages.ToList();
        _serverPendingMessages.Clear();
        foreach (var (type, payload) in pending)
        {
            // 跳过 GameStart：发牌动画已由第一个 GameStart 触发，后续的 GameStart 是冗余消息
            if (type == "GameStart") continue;
            DispatchServerMessage(type, payload);
        }
    }

    private void HandleServerPlayerReady(System.Text.Json.JsonElement root)
    {
        int readyCount = root.GetProperty("readyCount").GetInt32();
        int totalNeeded = root.GetProperty("totalNeeded").GetInt32();
        StatusMessage = $"等待玩家准备... ({readyCount}/{totalNeeded})";

        // 同步大厅列表并确保可见
        SyncLobbyFromAdapter();
        IsLobbyVisible = true;
    }

    private void HandleServerGameRestart(System.Text.Json.JsonElement root)
    {
        var message = root.TryGetProperty("message", out var m) ? m.GetString() : "重新发牌";
        StatusMessage = message;

        // 重置叫分/出牌UI，等待新的 game_start 和 turn_change
        IsBidPanelVisible = false;
        IsPlayPanelVisible = false;
        foreach (var p in AllPlayers)
        {
            p.IsCurrentTurn = false;
            p.IsThinking = false;
            p.LastAction = "";
        }
    }

    // ========== 断线重连 / 投票 ==========

    private void HandleServerReconnected(System.Text.Json.JsonElement root)
    {
        StatusMessage = "重连成功！";
        IsVoteVisible = false;
        VoteStatusMessage = "";

        if (!root.TryGetProperty("game_state", out var state)) return;

        // 恢复手牌
        var localSeat = 0; // 自己始终是本地座位0
        if (state.TryGetProperty("hand", out var handArr))
        {
            AllPlayers[localSeat].Hand.Clear();
            int idx = 0;
            foreach (var c in handArr.EnumerateArray())
            {
                var suit = (LolitaPoker.Core.Enums.Suit)c.GetProperty("suit").GetInt32();
                var rank = (LolitaPoker.Core.Enums.Rank)c.GetProperty("rank").GetInt32();
                var card = new LolitaPoker.Core.Models.Card(suit, rank);
                AllPlayers[localSeat].Hand.Add(new CardViewModel(card, true));
                idx++;
            }
            AllPlayers[localSeat].CardCount = idx;
        }

        // 恢复对手手牌数
        if (state.TryGetProperty("opponent_counts", out var counts) &&
            state.TryGetProperty("opponent_seats", out var seats))
        {
            var countList = counts.EnumerateArray().ToList();
            var seatList = seats.EnumerateArray().ToList();
            for (int i = 0; i < countList.Count && i < seatList.Count; i++)
            {
                int serverSeat = seatList[i].GetInt32();
                int local = ServerSeatToLocal(serverSeat);
                if (local >= 0 && local < 3)
                    AllPlayers[local].CardCount = countList[i].GetInt32();
            }
        }

        // 恢复玩家名
        if (state.TryGetProperty("player_names", out var names))
        {
            var nameArr = names.EnumerateArray().ToArray();
            for (int s = 0; s < 3 && s < nameArr.Length; s++)
            {
                int local = ServerSeatToLocal(s);
                if (local >= 0 && local < 3)
                    AllPlayers[local].Name = nameArr[s].GetString() ?? "";
            }
        }

        // 恢复地主信息
        if (state.TryGetProperty("landlord_seat", out var ll) && ll.ValueKind != JsonValueKind.Null)
        {
            int llLocal = ServerSeatToLocal(ll.GetInt32());
            if (llLocal >= 0 && llLocal < 3)
                AllPlayers[llLocal].Role = PlayerRole.Landlord;
        }

        // 恢复当前回合
        if (state.TryGetProperty("current_player", out var cp))
        {
            int cpLocal = ServerSeatToLocal(cp.GetInt32());
            for (int i = 0; i < 3; i++)
                AllPlayers[i].IsCurrentTurn = (i == cpLocal);
        }

        // 恢复阶段
        if (state.TryGetProperty("phase", out var phase))
        {
            string phaseStr = phase.GetString() ?? "playing";
            CurrentPhase = phaseStr == "bidding" ? GamePhase.Bidding : GamePhase.Playing;
            IsBidPanelVisible = (CurrentPhase == GamePhase.Bidding);
            IsPlayPanelVisible = (CurrentPhase == GamePhase.Playing);
        }
    }

    private void HandleServerVoteStart(System.Text.Json.JsonElement root)
    {
        var name = root.TryGetProperty("disconnected_player", out var n) ? n.GetString() : "某玩家";
        VoteMessage = $"玩家 {name} 断开连接\n选择结束对局或等待重连";
        VoteStatusMessage = "等待投票中...";
        IsVoteVisible = true;
    }

    private void HandleServerVoteUpdate(System.Text.Json.JsonElement root)
    {
        if (root.TryGetProperty("choice", out var choice))
        {
            string choiceStr = choice.GetString() ?? "";
            VoteStatusMessage = choiceStr == "end" ? "有玩家选择结束对局" : "等待其他玩家投票...";
        }
    }

    private void HandleServerReconnectWaiting(System.Text.Json.JsonElement root)
    {
        var seconds = root.TryGetProperty("timeout_seconds", out var s) ? s.GetInt32() : 60;
        VoteStatusMessage = $"等待玩家重连中... ({seconds}秒)";
        VoteMessage = "等待断线玩家重连";
    }

    private void HandleServerPlayerReconnected(System.Text.Json.JsonElement root)
    {
        var name = root.TryGetProperty("player_name", out var n) ? n.GetString() : "玩家";
        StatusMessage = $"{name} 已重连";
        IsVoteVisible = false;
        VoteStatusMessage = "";
    }

    private void HandleServerGameEnded(System.Text.Json.JsonElement root)
    {
        IsVoteVisible = false;
        VoteStatusMessage = "";
        IsBidPanelVisible = false;
        IsPlayPanelVisible = false;
        IsLobbyVisible = false;

        // 显示对局结束提示（复用 IsGameOver 区域）
        StatusMessage = "对局已结束（玩家断线）";
        IsGameOver = true;
        IsNewGameVisible = false;
    }

    private async System.Threading.Tasks.Task SubmitVote(string choice)
    {
        if (_networkAdapter is WebSocketNetworkAdapter ws)
        {
            await ws.SendReconnectVoteAsync(choice);
        }
        VoteStatusMessage = choice == "end" ? "你选择了结束对局" : "你选择了等待重连";
    }

    private async void HandleServerGameStart(System.Text.Json.JsonElement root)
    {
        // 清理前一局动画残留
        _dealCts?.Cancel();
        _dealCts?.Dispose();
        _dealCts = null;
        _shuffleTcs?.TrySetResult();
        _shuffleTcs = null;
        _isDealing = false;
        _serverPendingMessages.Clear();

        // 重置出牌跟踪状态
        _lastServerCombo = null;
        _serverConsecutivePasses = 0;

        // 服务器发牌：接收手牌数据
        var handArray = root.GetProperty("hand");
        var hand = new List<Card>();
        foreach (var cardEl in handArray.EnumerateArray())
        {
            var suit = (Suit)cardEl.GetProperty("suit").GetInt32();
            var rank = (Rank)cardEl.GetProperty("rank").GetInt32();
            hand.Add(new Card(suit, rank));
        }

        // 同步玩家名称（服务器按座位顺序发送，需要映射到本地视觉位置）
        if (root.TryGetProperty("player_names", out var namesArray))
        {
            var names = namesArray.EnumerateArray().ToList();
            for (int serverSeat = 0; serverSeat < names.Count; serverSeat++)
            {
                int localSeat = ServerSeatToLocal(serverSeat);
                if (localSeat >= 0 && localSeat < 3)
                    AllPlayers[localSeat].Name = names[serverSeat].GetString() ?? AllPlayers[localSeat].Name;
            }
        }

        // 重置UI
        PlayerBottom.Hand.Clear();
        PlayerRight.Hand.Clear();
        PlayerLeft.Hand.Clear();
        LandlordCards.Clear();
        LandlordCardsVisible = true;
        IsNewGameVisible = false;
        IsGameOver = false;
        IsBidPanelVisible = false;
        IsPlayPanelVisible = false;
        IsLobbyVisible = false;
        IsReady = false;
        _selectionCache.Clear();

        // 清除适配器中所有玩家的准备标志（服务器已重置，本地也需同步）
        if (_networkAdapter is WebSocketNetworkAdapter wsAdapter)
        {
            foreach (var lp in wsAdapter.LobbyPlayers)
                lp.Ready = false;
        }

        foreach (var p in AllPlayers)
        {
            p.Role = PlayerRole.Farmer;
            p.LastAction = "";
            p.IsCurrentTurn = false;
            p.IsThinking = false;
        }

        // 设置底牌（背面）
        int kittyCount = 3;
        if (root.TryGetProperty("kitty_count", out var kc))
            kittyCount = kc.GetInt32();
        for (int i = 0; i < kittyCount; i++)
            LandlordCards.Add(new CardViewModel(new Card(Suit.None, Rank.Three), false));

        // 播放洗牌 → 发牌 → 翻牌动画
        _isDealing = true;
        _dealCts = new CancellationTokenSource();
        var ct = _dealCts.Token;

        try
        {
            // ── 阶段1: 洗牌动画 ──
            _shuffleTcs = new TaskCompletionSource();
            ShuffleRequested?.Invoke();

            // 等待洗牌动画完成（不依赖 Storyboard.Completed 回调，用固定延时）
            await Task.Delay(1800, ct);
            ct.ThrowIfCancellationRequested();

            // ── 阶段2: 逐张发牌（背面） ──
            StatusMessage = "发牌中...";
            CurrentPhase = GamePhase.Dealing;

            var handVms = hand.Select(c => new CardViewModel(c, faceUp: false)).ToList();

            for (int i = 0; i < handVms.Count; i++)
            {
                PlayerBottom.Hand.Add(handVms[i]);
                if (i < handVms.Count - 1)
                    await Task.Delay(60, ct);
            }

            // 强制 View 完成手牌区域布局，确保所有 CardControl 实例已创建
            // 这是必须的：如果 CardControl 尚未创建，PropertyChanged 事件无人订阅，动画无法触发
            HandLayoutRequested?.Invoke();
            await Task.Delay(50, ct);

            // 对手显示牌背
            for (int i = 1; i < 3; i++)
                AllPlayers[i].CardCount = 17;
            PlayerBottom.CardCount = hand.Count;

            // ── 阶段3: 聚拢玩家手牌 ──
            StatusMessage = "理牌中...";
            foreach (var cardVm in PlayerBottom.Hand)
            {
                cardVm.GatherOffset = -20;
                cardVm.AnimationState = CardAnimation.Gathering;
            }
            await Task.Delay(350, ct);

            // ── 阶段4: 依次翻牌 ──
            for (int i = 0; i < PlayerBottom.Hand.Count; i++)
            {
                PlayerBottom.Hand[i].IsFaceUp = true;
                PlayerBottom.Hand[i].AnimationState = CardAnimation.Revealing;
                await Task.Delay(20, ct);
            }

            // 等待最后一张翻牌+铺开动画完成
            await Task.Delay(600, ct);

            // ── 阶段5: 通知服务器发牌动画完成 ──
            _isDealing = false;
            FlushServerPendingEvents();

            if (_networkAdapter != null)
            {
                _ = _networkAdapter.SendMessageAsync(new NetworkMessage
                {
                    Type = "dealing_complete",
                    Payload = "{}"
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 动画被取消
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[GameVM] 发牌动画异常: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            _isDealing = false;
            _dealCts?.Dispose();
            _dealCts = null;
            FlushServerPendingEvents();
        }
    }

    private void HandleServerTurnChange(System.Text.Json.JsonElement root)
    {
        int serverSeat = root.GetProperty("current_player").GetInt32();
        string phase = root.GetProperty("phase").GetString() ?? "playing";
        int localSeat = ServerSeatToLocal(serverSeat);

        // 读取出牌超时设置
        if (root.TryGetProperty("turn_timeout", out var ttEl))
            TurnTimeoutSeconds = ttEl.GetInt32();

        foreach (var p in AllPlayers)
            p.IsCurrentTurn = p.SeatIndex == localSeat;

        // 清空当前玩家的出牌区域
        AllPlayers[localSeat].PlayedCards.Clear();
        AllPlayers[localSeat].LastAction = "";
        AllPlayers[localSeat].IsThinking = false;

        if (phase == "bidding")
        {
            CurrentPhase = GamePhase.Bidding;
            if (localSeat == 0)
            {
                IsBidPanelVisible = true;
                StatusMessage = "请叫分 (1-3 分，或不叫)";
                CanBid1 = true;
                CanBid2 = true;
                CanBid3 = true;
            }
            else
            {
                IsBidPanelVisible = false;
                AllPlayers[localSeat].IsThinking = true;
                StatusMessage = $"{AllPlayers[localSeat].Name} 思考中...";
            }
        }
        else if (phase == "playing")
        {
            CurrentPhase = GamePhase.Playing;
            if (localSeat == 0)
            {
                IsPlayPanelVisible = true;
                _hints = new List<CardCombo>();
                _hintIndex = 0;
                HintNoPlayMessage = "";
                _selectionCache.ApplyToHand(PlayerBottom.Hand);
                CommandManager.InvalidateRequerySuggested();
                StatusMessage = "你的回合，请出牌";
                StartCountdown();
            }
            else
            {
                IsPlayPanelVisible = false;
                StopCountdown();
                AllPlayers[localSeat].IsThinking = true;
                StatusMessage = $"{AllPlayers[localSeat].Name} 思考中...";
            }
        }
    }

    private void HandleServerBidUpdate(System.Text.Json.JsonElement root)
    {
        int serverSeat = root.GetProperty("player_seat").GetInt32();
        int localSeat = ServerSeatToLocal(serverSeat);
        int amount = root.GetProperty("amount").GetInt32();

        if (amount > 0)
            AllPlayers[localSeat].LastAction = $"叫 {amount} 分";
        else
            AllPlayers[localSeat].LastAction = "不叫";

        AllPlayers[localSeat].IsThinking = false;
        IsBidPanelVisible = false;
    }

    private void HandleServerLandlordAssigned(System.Text.Json.JsonElement root)
    {
        int serverSeat = root.GetProperty("seat").GetInt32();
        int localSeat = ServerSeatToLocal(serverSeat);
        int multiplier = root.GetProperty("multiplier").GetInt32();

        // 设置角色
        for (int i = 0; i < 3; i++)
            AllPlayers[i].Role = i == localSeat ? PlayerRole.Landlord : PlayerRole.Farmer;

        // 如果自己是地主，更新手牌
        if (localSeat == 0 && root.TryGetProperty("kitty", out var kittyArray))
        {
            foreach (var cardEl in kittyArray.EnumerateArray())
            {
                var suit = (Suit)cardEl.GetProperty("suit").GetInt32();
                var rank = (Rank)cardEl.GetProperty("rank").GetInt32();
                PlayerBottom.Hand.Add(new CardViewModel(new Card(suit, rank), true));
            }
            // 重新排序手牌
            var sorted = PlayerBottom.Hand
                .OrderByDescending(c => c.Model.Rank)
                .ThenBy(c => c.Model.Suit)
                .ToList();
            PlayerBottom.Hand.Clear();
            foreach (var c in sorted) PlayerBottom.Hand.Add(c);
            PlayerBottom.CardCount = PlayerBottom.Hand.Count;
        }

        // 显示底牌（正面）
        LandlordCards.Clear();
        if (root.TryGetProperty("kitty", out var kittyArr))
        {
            foreach (var cardEl in kittyArr.EnumerateArray())
            {
                var suit = (Suit)cardEl.GetProperty("suit").GetInt32();
                var rank = (Rank)cardEl.GetProperty("rank").GetInt32();
                LandlordCards.Add(new CardViewModel(new Card(suit, rank), true));
            }
        }

        var landlordName = root.TryGetProperty("player_name", out var pn) ? pn.GetString() ?? "" : AllPlayers[localSeat].Name;
        LandlordLabel = $"👑 {landlordName} 是地主";
        LandlordCardsVisible = true;
        IsBidPanelVisible = false;
        StatusMessage = $"{landlordName} 成为地主！({multiplier}倍)";
    }

    private void HandleServerCardsPlayed(System.Text.Json.JsonElement root)
    {
        int serverSeat = root.GetProperty("seat").GetInt32();
        int localSeat = ServerSeatToLocal(serverSeat);
        int cardCount = root.GetProperty("card_count").GetInt32();

        var cards = new List<Card>();
        foreach (var cardEl in root.GetProperty("cards").EnumerateArray())
        {
            var suit = (Suit)cardEl.GetProperty("suit").GetInt32();
            var rank = (Rank)cardEl.GetProperty("rank").GetInt32();
            cards.Add(new Card(suit, rank));
        }

        // 记录上一手出牌，供提示系统使用
        _lastServerCombo = RulesEngine.ClassifyPlay(cards);
        _serverConsecutivePasses = 0;

        AllPlayers[localSeat].LastAction = "";
        AllPlayers[localSeat].PlayedCards.Clear();
        AllPlayers[localSeat].IsThinking = false;
        foreach (var card in cards)
        {
            AllPlayers[localSeat].PlayedCards.Add(new CardViewModel(card, true) { IsPlayable = false });
        }

        // 如果是自己出的牌，从手牌中移除
        if (localSeat == 0)
        {
            var toRemove = new HashSet<(Suit, Rank)>(cards.Select(c => (c.Suit, c.Rank)));
            var toRemoveVm = PlayerBottom.Hand
                .Where(vm => toRemove.Contains((vm.Model.Suit, vm.Model.Rank)))
                .ToList();
            foreach (var vm in toRemoveVm)
                PlayerBottom.Hand.Remove(vm);
            PlayerBottom.CardCount = PlayerBottom.Hand.Count;
            IsPlayPanelVisible = false;
        }
        else
        {
            AllPlayers[localSeat].CardCount = cardCount;
        }
    }

    private void HandleServerPlayerPassed(System.Text.Json.JsonElement root)
    {
        int serverSeat = root.GetProperty("seat").GetInt32();
        int localSeat = ServerSeatToLocal(serverSeat);
        AllPlayers[localSeat].LastAction = "不出";
        AllPlayers[localSeat].PlayedCards.Clear();
        AllPlayers[localSeat].IsThinking = false;

        // 连续两人不出，清空上家出牌（与服务端 game_logic.py 逻辑一致）
        _serverConsecutivePasses++;
        if (_serverConsecutivePasses >= 2)
        {
            _lastServerCombo = null;
            _serverConsecutivePasses = 0;
        }

        if (localSeat == 0)
            IsPlayPanelVisible = false;
    }

    private void HandleServerGameOver(System.Text.Json.JsonElement root)
    {
        int serverWinnerSeat = root.GetProperty("winner_seat").GetInt32();
        int winnerSeat = ServerSeatToLocal(serverWinnerSeat);
        string winnerRole = root.GetProperty("winner_role").GetString() ?? "";
        int multiplier = root.GetProperty("multiplier").GetInt32();

        _aiTimer.Stop();
        IsPlayPanelVisible = false;
        IsBidPanelVisible = false;
        CurrentPhase = GamePhase.GameOver;

        bool playerIsLandlord = PlayerBottom.Role == PlayerRole.Landlord;
        string msg;

        if (winnerSeat == 0)
        {
            msg = $"🎉 你赢了！（{multiplier}倍）";
        }
        else if (winnerRole == "landlord")
        {
            msg = playerIsLandlord
                ? $"🎉 你赢了！（{multiplier}倍）"
                : $"地主 {AllPlayers[winnerSeat].Name} 获胜！你输了（{multiplier}倍）";
        }
        else
        {
            msg = playerIsLandlord
                ? $"农民获胜！你输了（{multiplier}倍）"
                : $"🎉 农民获胜！你赢了（{multiplier}倍）";
        }

        StatusMessage = msg;
        IsGameOver = true;
        LandlordCardsVisible = true;
    }

    // ========== 资源清理 ==========

    /// <summary>
    /// 清理所有资源，防止内存泄漏。返回菜单或窗口关闭时调用。
    /// </summary>
    public void Cleanup()
    {
        // 停止倒计时、TTS 和 BGM
        StopCountdown();
        _ttsReadyTcs?.TrySetResult();
        _ttsReadyTcs = null;
        _bgmService.Stop();

        // 停止AI定时器
        _aiTimer.Stop();
        _aiTimer.Tick -= OnAiTimerTick;

        // 取消发牌动画
        _dealCts?.Cancel();
        _dealCts?.Dispose();
        _dealCts = null;

        // 取消 GameManager 事件订阅
        _gameManager.PhaseChanged -= OnPhaseChanged;
        _gameManager.TurnChanged -= OnTurnChanged;
        _gameManager.PlayerPlayed -= OnPlayerPlayed;
        _gameManager.CardsChanged -= OnCardsChanged;
        _gameManager.GameEnded -= OnGameEnded;
        _gameManager.MessageChanged -= msg => StatusMessage = msg;

        // 取消选牌缓存事件订阅
        _selectionCache.SelectionChanged -= OnSelectionChanged;

        // 取消静态事件订阅（关键！防止内存泄漏）
        CardViewModel.SelectionStateChanged -= OnCardSelectionChanged;

        // 取消网络事件订阅
        if (_networkGameManager != null)
        {
            _networkGameManager.GameStateReceived -= OnNetworkGameStateReceived;
            _networkGameManager.OpponentActionReceived -= OnNetworkOpponentAction;
            _networkGameManager.PlayerJoined -= OnNetworkPlayerJoined;
            _networkGameManager.PlayerLeft -= OnNetworkPlayerLeft;
            _networkGameManager.ServerMessageReceived -= OnServerMessageReceived;
        }

        // 断开网络连接
        if (_networkAdapter != null)
        {
            _ = _networkAdapter.DisconnectAsync();
            if (_networkAdapter is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
