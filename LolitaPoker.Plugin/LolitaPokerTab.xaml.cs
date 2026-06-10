// -----------------------------------------------------------------------
// LolitaPokerTab.xaml.cs - 访客表 Tab 控件
// 显示座位状态、准备按钮、房主开始游戏按钮
// -----------------------------------------------------------------------

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VPet_Simulator.Windows.Interface;

namespace LolitaPoker.Plugin;

public partial class LolitaPokerTab : UserControl, INotifyPropertyChanged
{
    private readonly IMPWindows _mpWindows;
    private readonly VPetNetworkAdapter _adapter;
    private readonly ulong _localSteamId;

    private readonly bool[] _readySeats = new bool[3];
    private bool _localReady;
    private string _statusMessage = "等待玩家加入…";

    // ========== 座位显示 ==========

    public string Seat0Text
    {
        get
        {
            var name = GetSeatPlayerName(0);
            var ready = _readySeats[0] ? " ✅" : "";
            return $"座位 0（房主）: {name}{ready}";
        }
    }

    public string Seat1Text
    {
        get
        {
            var name = GetSeatPlayerName(1);
            var ready = _readySeats[1] ? " ✅" : "";
            return $"座位 1: {name}{ready}";
        }
    }

    public string Seat2Text
    {
        get
        {
            var name = GetSeatPlayerName(2);
            var ready = _readySeats[2] ? " ✅" : "";
            return $"座位 2: {name}{ready}";
        }
    }

    public Brush Seat0Color => _adapter.ReverseSeatMap.ContainsKey(0) ? GetReadyColor(0) : Brushes.Gray;
    public Brush Seat1Color => _adapter.ReverseSeatMap.ContainsKey(1) ? GetReadyColor(1) : Brushes.Gray;
    public Brush Seat2Color => _adapter.ReverseSeatMap.ContainsKey(2) ? GetReadyColor(2) : Brushes.Gray;

    // ========== 准备按钮 ==========

    public string ReadyButtonText => _localReady ? "❌ 取消准备" : "✅ 准备";
    public string ReadyButtonColor => _localReady ? "#e74c3c" : "#27ae60";

    // ========== 开始按钮 ==========

    public bool IsStartButtonVisible => _mpWindows.IsHost;

    public ICommand ToggleReadyCommand { get; }
    public ICommand StartGameCommand { get; }

    // ========== 状态消息 ==========

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    // ========== 事件 ==========

    /// <summary>房主点击开始游戏时触发</summary>
    public event Action? OnStartGameRequested;

    public LolitaPokerTab(IMPWindows mpWindows, VPetNetworkAdapter adapter)
    {
        InitializeComponent();
        DataContext = this;

        _mpWindows = mpWindows;
        _adapter = adapter;
        _localSteamId = adapter.LocalPlayerId != null ? ulong.Parse(adapter.LocalPlayerId) : 0;

        ToggleReadyCommand = new RelayCommand(_ => ToggleReady());
        StartGameCommand = new RelayCommand(_ => OnStartGameRequested?.Invoke(), _ => CanStartGame());

        // 监听访客表消息
        _mpWindows.ReceivedMessage += OnMessageReceived;
        _mpWindows.OnMemberJoined += OnMemberJoined;
        _mpWindows.OnMemberLeave += OnMemberLeave;
    }

    // ========== 操作 ==========

    private void ToggleReady()
    {
        _localReady = !_localReady;

        var msg = new MPMessage
        {
            Type = _localReady ? VpetMpTypes.GameReady : VpetMpTypes.GameUnready
        };

        _mpWindows.SendMessageALL(msg);
        UpdateDisplay();
    }

    private bool CanStartGame()
    {
        if (!_mpWindows.IsHost) return false;
        // 至少 3 人且全部准备
        return _adapter.ReverseSeatMap.Count >= 3
            && _readySeats[0] && _readySeats[1] && _readySeats[2];
    }

    // ========== 消息处理 ==========

    private void OnMessageReceived(ulong senderSteamId, MPMessage msg)
    {
        Dispatcher.Invoke(() =>
        {
            switch (msg.Type)
            {
                case VpetMpTypes.GameReady:
                    HandleReady(senderSteamId, true);
                    break;
                case VpetMpTypes.GameUnready:
                    HandleReady(senderSteamId, false);
                    break;
                case VpetMpTypes.PlayerReadyState:
                    HandleReadyState(msg);
                    break;
                case VpetMpTypes.StartGame:
                    StatusMessage = "🎮 游戏进行中";
                    break;
            }
        });
    }

    private void HandleReady(ulong steamId, bool ready)
    {
        var seat = _adapter.GetSeat(steamId);
        if (seat < 0 || seat >= 3) return;

        _readySeats[seat] = ready;

        // 房主广播准备状态
        if (_mpWindows.IsHost)
        {
            var stateMsg = new MPMessage { Type = VpetMpTypes.PlayerReadyState };
            stateMsg.SetContent(new ReadyStatePayload
            {
                ReadyCount = _readySeats.Count(r => r),
                ReadySeats = _readySeats.ToArray()
            });
            _mpWindows.SendMessageALL(stateMsg);
        }

        UpdateDisplay();
    }

    private void HandleReadyState(MPMessage msg)
    {
        try
        {
            var payload = msg.GetContent<ReadyStatePayload>();
            if (payload.ReadySeats != null && payload.ReadySeats.Length >= 3)
            {
                Array.Copy(payload.ReadySeats, _readySeats, 3);
            }
            UpdateDisplay();
        }
        catch
        {
            // 解析失败忽略
        }
    }

    private void OnMemberJoined(ulong steamId)
    {
        Dispatcher.Invoke(UpdateDisplay);
    }

    private void OnMemberLeave(ulong steamId)
    {
        Dispatcher.Invoke(() =>
        {
            var seat = _adapter.GetSeat(steamId);
            if (seat >= 0 && seat < 3)
            {
                _readySeats[seat] = false;
            }
            UpdateDisplay();
        });
    }

    // ========== UI 更新 ==========

    private void UpdateDisplay()
    {
        OnPropertyChanged(nameof(Seat0Text));
        OnPropertyChanged(nameof(Seat1Text));
        OnPropertyChanged(nameof(Seat2Text));
        OnPropertyChanged(nameof(Seat0Color));
        OnPropertyChanged(nameof(Seat1Color));
        OnPropertyChanged(nameof(Seat2Color));
        OnPropertyChanged(nameof(ReadyButtonText));
        OnPropertyChanged(nameof(ReadyButtonColor));
        OnPropertyChanged(nameof(IsStartButtonVisible));

        // 更新状态消息
        int playerCount = _adapter.ReverseSeatMap.Count;
        int readyCount = _readySeats.Count(r => r);

        if (playerCount < 3)
        {
            StatusMessage = $"等待玩家加入… ({playerCount}/3)";
        }
        else if (readyCount < 3)
        {
            StatusMessage = $"等待全部准备… ({readyCount}/3)";
        }
        else
        {
            StatusMessage = _mpWindows.IsHost
                ? "所有人已准备，点击「开始游戏」"
                : "所有人已准备，等待房主开始…";
        }

        (StartGameCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private string GetSeatPlayerName(int seat)
    {
        if (!_adapter.ReverseSeatMap.TryGetValue(seat, out var steamId))
            return "空";

        foreach (var friend in _mpWindows.Friends)
        {
            if (friend.FriendID == steamId)
                return friend.Name;
        }

        // 自己
        if (steamId == _localSteamId)
            return "你";

        return steamId.ToString();
    }

    private Brush GetReadyColor(int seat)
    {
        return _readySeats[seat] ? Brushes.LimeGreen : Brushes.Orange;
    }

    // ========== 清理 ==========

    public void Cleanup()
    {
        _mpWindows.ReceivedMessage -= OnMessageReceived;
        _mpWindows.OnMemberJoined -= OnMemberJoined;
        _mpWindows.OnMemberLeave -= OnMemberLeave;
    }

    // ========== INotifyPropertyChanged ==========

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// 简单的 RelayCommand 实现（避免依赖 LolitaPoker.Core 的 RelayCommand）
/// </summary>
internal class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    // WPF ICommand infrastructure
    void ICommand.Execute(object? parameter) => Execute(parameter);
    bool ICommand.CanExecute(object? parameter) => CanExecute(parameter);
}
