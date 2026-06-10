// -----------------------------------------------------------------------
// LolitaPokerPlugin.cs - VPet 插件入口
// 继承 MainPlugin，订阅 MutiPlayerHandle 接入访客表
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Windows;
using LolitaPoker.Core.Audio;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Network;
using LolitaPoker.Core.ViewModels;
using VPet_Simulator.Windows.Interface;

namespace LolitaPoker.Plugin;

/// <summary>
/// 萝莉丝扑克 VPet 插件。
/// 在访客表中注入「萝莉丝扑克」Tab，通过 Steam P2P 进行斗地主联机对战。
/// </summary>
public class LolitaPokerPlugin : MainPlugin
{
    public override string PluginName => "LolitaPoker";

    private IMPWindows? _currentLobby;
    private VPetNetworkAdapter? _adapter;
    private HostGameManager? _hostGameManager;
    private GameHostWindow? _gameWindow;
    private LolitaPokerTab? _tab;

    public LolitaPokerPlugin(IMainWindow mainwin) : base(mainwin) { }

    public override void LoadPlugin()
    {
        // 订阅访客表事件
        MW.MutiPlayerHandle += OnMutiPlayerHandle;
        Debug.WriteLine("[LolitaPokerPlugin] 插件已加载，监听访客表事件");
    }

    public override void EndGame()
    {
        Cleanup();
        Debug.WriteLine("[LolitaPokerPlugin] 插件已卸载");
    }

    private void OnMutiPlayerHandle(IMPWindows mp)
    {
        // 清理旧连接
        CleanupCurrentLobby();

        _currentLobby = mp;

        var localSteamId = mp.SelftoIMPFriend().FriendID;
        var isHost = mp.IsHost;

        // 创建网络适配器
        _adapter = new VPetNetworkAdapter(mp, localSteamId, isHost);

        // 创建并注入 Tab
        _tab = new LolitaPokerTab(mp, _adapter);
        _tab.OnStartGameRequested += OnStartGameRequested;

        var tabItem = new System.Windows.Controls.TabItem
        {
            Header = "🃏 萝莉丝扑克",
            Content = _tab
        };

        mp.TabControl.Items.Add(tabItem);

        // 监听访客表关闭
        mp.ClosingMutiPlayer += OnLobbyClosing;

        // 非房主：提前监听 StartGame 消息（房主可能在我们准备前就开始游戏）
        if (!isHost)
        {
            _adapter.OnMessageReceived += OnNonHostMessageReceived;
        }

        Debug.WriteLine($"[LolitaPokerPlugin] 访客表已创建/加入 (LobbyID={mp.LobbyID}, IsHost={isHost})");
    }

    /// <summary>
    /// 房主点击「开始游戏」
    /// </summary>
    private void OnStartGameRequested()
    {
        if (_currentLobby == null || _adapter == null) return;

        if (_currentLobby.IsGameRunning)
        {
            MessageBox.Show("另一个游戏正在进行中，请等待结束。", "萝莉丝扑克", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 检查人数
        if (_adapter.ReverseSeatMap.Count < 3)
        {
            MessageBox.Show("需要 3 位玩家才能开始游戏。", "萝莉丝扑克", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _currentLobby.IsGameRunning = true;

        // 作为房主：创建 HostGameManager
        if (_currentLobby.IsHost)
        {
            _hostGameManager = new HostGameManager(_adapter, GetSelfName());

            // 设置玩家名称
            foreach (var friend in _currentLobby.Friends)
            {
                var seat = _adapter.GetSeat(friend.FriendID);
                if (seat >= 0)
                    _hostGameManager.SetPlayerName(seat, friend.Name);
            }

            // 创建游戏 ViewModel
            var gameVm = CreateGameViewModel();
            gameVm.GameMode = GameMode.VPetLan;
            gameVm.InitializePlayers(GetSelfName(), GetPlayerName(1), GetPlayerName(2));
            gameVm.InitializeNetworkMode(_adapter);

            // 设置座位映射（用于 GetPlayerIndex）
            var seatMap = _adapter.SeatMap.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            gameVm.SetPlayerSeatMap(seatMap);

            gameVm.RequestNavigateToMenu = () =>
            {
                _gameWindow?.Close();
            };

            // 弹出游戏窗口
            ShowGameWindow(gameVm);

            // 开始游戏（使用确定性种子，非房主会收到相同种子）
            _hostGameManager.StartNewGame();

            // 监听 adapter 消息，路由到 HostGameManager
            _adapter.OnMessageReceived += OnHostMessageReceived;
        }
        else
        {
            // 非房主：等待 StartGame 消息，adapter 的 OnMessageReceived 会触发
            // NetworkGameManager 接收 "new_game" 消息并调用 StartNewGame(seed, firstPlayer)
            var gameVm = CreateGameViewModel();
            gameVm.GameMode = GameMode.VPetLan;

            var playerNames = _adapter.LastPlayerNames;
            if (playerNames != null && playerNames.Length >= 3)
                gameVm.InitializePlayers(playerNames[0], playerNames[1], playerNames[2]);
            else
                gameVm.InitializePlayers(GetSelfName(), GetPlayerName(1), GetPlayerName(2));

            gameVm.InitializeNetworkMode(_adapter);

            // 设置座位映射
            var seatMap = _adapter.SeatMap.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            gameVm.SetPlayerSeatMap(seatMap);

            gameVm.RequestNavigateToMenu = () => { _gameWindow?.Close(); };

            ShowGameWindow(gameVm);
        }
    }

    /// <summary>
    /// 非房主早期监听：收到 StartGame 消息时自动创建游戏窗口
    /// </summary>
    private void OnNonHostMessageReceived(NetworkMessage msg)
    {
        if (msg.Type != "new_game" || _adapter == null || _currentLobby == null) return;
        if (_gameWindow != null) return; // 已经在游戏窗口中

        // 取消早期监听（后续消息由 NetworkGameManager 处理）
        _adapter.OnMessageReceived -= OnNonHostMessageReceived;

        // 解析种子
        int seed = 0, firstPlayer = 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(msg.Payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("Seed", out var s)) seed = s.GetInt32();
            if (root.TryGetProperty("FirstPlayerIndex", out var f)) firstPlayer = f.GetInt32();
        }
        catch { }

        // 创建游戏窗口
        Application.Current.Dispatcher.Invoke(() =>
        {
            _currentLobby.IsGameRunning = true;

            var gameVm = CreateGameViewModel();
            gameVm.GameMode = GameMode.VPetLan;

            var playerNames = _adapter.LastPlayerNames;
            if (playerNames != null && playerNames.Length >= 3)
                gameVm.InitializePlayers(playerNames[0], playerNames[1], playerNames[2]);
            else
                gameVm.InitializePlayers(GetSelfName(), GetPlayerName(1), GetPlayerName(2));

            gameVm.InitializeNetworkMode(_adapter);

            var seatMap = _adapter.SeatMap.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
            gameVm.SetPlayerSeatMap(seatMap);

            gameVm.RequestNavigateToMenu = () => { _gameWindow?.Close(); };

            ShowGameWindow(gameVm);

            // NetworkGameManager 已订阅 adapter，但 "new_game" 消息已被消费
            // 需要手动启动游戏（使用种子确保手牌一致）
            gameVm.StartNewGameWithSeed(seed, firstPlayer);
        });
    }

    /// <summary>
    /// HostGameManager 路由：将 adapter 消息转发给 HostGameManager
    /// </summary>
    private void OnHostMessageReceived(NetworkMessage msg)
    {
        if (_hostGameManager == null || _adapter == null) return;

        var seat = ulong.TryParse(msg.SenderId, out var sid)
            ? _adapter.GetSeat(sid) : -1;

        if (seat < 0) return;

        switch (msg.Type)
        {
            case "bid":
                if (System.Text.Json.JsonSerializer.Deserialize<BidPayload>(msg.Payload) is BidPayload bid)
                    _hostGameManager.HandleBid(seat, bid.Amount);
                break;
            case "play":
                if (System.Text.Json.JsonSerializer.Deserialize<PlayCardsPayload>(msg.Payload) is PlayCardsPayload play)
                    _hostGameManager.HandlePlay(seat, play.Cards);
                break;
            case "pass":
                _hostGameManager.HandlePass(seat);
                break;
        }
    }

    // ========== 窗口管理 ==========

    private void ShowGameWindow(GameViewModel gameVm)
    {
        _gameWindow = new GameHostWindow(gameVm, OnGameWindowClosed);
        _gameWindow.Show();
        MW.Windows.Add(_gameWindow);
    }

    private void OnGameWindowClosed()
    {
        if (_currentLobby != null)
        {
            _currentLobby.IsGameRunning = false;
        }

        _hostGameManager?.Dispose();
        _hostGameManager = null;
        _gameWindow = null;

        // 取消消息路由
        if (_adapter != null)
        {
            _adapter.OnMessageReceived -= OnHostMessageReceived;
        }
    }

    private GameViewModel CreateGameViewModel()
    {
        return new GameViewModel(NullTtsService.Instance, NullBgmService.Instance, NullSoundEffectService.Instance);
    }

    // ========== 辅助方法 ==========

    private string GetSelfName()
    {
        if (_currentLobby == null) return "玩家";
        var self = _currentLobby.SelftoIMPFriend();
        return self.Name ?? "玩家";
    }

    private string GetPlayerName(int seat)
    {
        if (_currentLobby == null || _adapter == null) return "等待加入…";

        if (_adapter.ReverseSeatMap.TryGetValue(seat, out var steamId))
        {
            foreach (var friend in _currentLobby.Friends)
            {
                if (friend.FriendID == steamId)
                    return friend.Name ?? "未知";
            }
        }

        return "等待加入…";
    }

    // ========== 清理 ==========

    private void OnLobbyClosing()
    {
        CleanupCurrentLobby();
    }

    private void CleanupCurrentLobby()
    {
        _tab?.Cleanup();
        _tab = null;

        _gameWindow?.Close();
        _gameWindow = null;

        _hostGameManager?.Dispose();
        _hostGameManager = null;

        _adapter?.Dispose();
        _adapter = null;

        if (_currentLobby != null)
        {
            _currentLobby.ClosingMutiPlayer -= OnLobbyClosing;
            _currentLobby = null;
        }
    }

    private void Cleanup()
    {
        CleanupCurrentLobby();
        MW.MutiPlayerHandle -= OnMutiPlayerHandle;
    }
}
