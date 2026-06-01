// -----------------------------------------------------------------------
// NetworkSettingsViewModel.cs - 网络连接设置视图模型
// 用于 P2P 和服务器模式的连接配置页面
// -----------------------------------------------------------------------

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Network;

namespace LolitaPoker.Core.ViewModels;

/// <summary>
/// 网络连接设置视图模型（P2P 和服务器共用）
/// </summary>
public class NetworkSettingsViewModel : ViewModelBase
{
    private readonly Action<GameMode, INetworkAdapter?, string, string, int, string> _navigate;
    private readonly Action _backToMenu;
    private readonly GameConfig _config;

    // ========== 模式 ==========
    private GameMode _mode;
    public GameMode Mode
    {
        get => _mode;
        set
        {
            SetProperty(ref _mode, value);
            OnPropertyChanged(nameof(IsP2PMode));
            OnPropertyChanged(nameof(IsServerMode));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(TitleEmoji));
        }
    }

    public bool IsP2PMode => _mode == GameMode.P2P;
    public bool IsServerMode => _mode == GameMode.Server;

    public string TitleEmoji => _mode == GameMode.P2P ? "📡" : "🌐";
    public string Title => _mode == GameMode.P2P ? "P2P 联网模式" : "服务器模式";
    public string Subtitle => _mode == GameMode.P2P
        ? "局域网内直接连接对战（开发中）"
        : "连接远程服务器，与全球玩家对战";

    // ========== 共用 ==========
    private string _connectionStatus = "";
    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => SetProperty(ref _connectionStatus, value);
    }

    private string _connectionStatusColor = "#e74c3c";
    public string ConnectionStatusColor
    {
        get => _connectionStatusColor;
        set => SetProperty(ref _connectionStatusColor, value);
    }

    private bool _isConnecting;
    public bool IsConnecting
    {
        get => _isConnecting;
        set => SetProperty(ref _isConnecting, value);
    }

    // ========== P2P 设置 ==========
    private string _ipAddress = "127.0.0.1";
    public string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (SetProperty(ref _ipAddress, value))
                SaveConfig();
        }
    }

    private string _portText = "9000";
    public string PortText
    {
        get => _portText;
        set
        {
            if (SetProperty(ref _portText, value))
                SaveConfig();
        }
    }

    // ========== 服务器设置 ==========
    private string _serverUrl = "ws://127.0.0.1:8000/ws";
    public string ServerUrl
    {
        get => _serverUrl;
        set
        {
            if (SetProperty(ref _serverUrl, value))
                SaveConfig();
        }
    }

    private string _roomCode = "";
    public string RoomCode
    {
        get => _roomCode;
        set => SetProperty(ref _roomCode, value);
    }

    // ========== 列表服务器 ==========
    private string _masterUrl = "ws://127.0.0.1:8000/ws/lobby";
    public string MasterUrl
    {
        get => _masterUrl;
        set
        {
            if (SetProperty(ref _masterUrl, value))
                SaveConfig();
        }
    }

    // ========== 房间可见性（创建房间时） ==========
    private bool _isPublicRoom = true;
    public bool IsPublicRoom
    {
        get => _isPublicRoom;
        set => SetProperty(ref _isPublicRoom, value);
    }

    // ========== 房间列表（大厅） ==========
    private readonly ObservableCollection<RoomListEntry> _roomList = new();
    public ReadOnlyObservableCollection<RoomListEntry> RoomList { get; }

    private bool _isLoadingRooms;
    public bool IsLoadingRooms
    {
        get => _isLoadingRooms;
        set => SetProperty(ref _isLoadingRooms, value);
    }

    public bool HasRooms => _roomList.Count > 0;

    // ========== 服务器浏览器 ==========
    private readonly ObservableCollection<ServerBrowserEntry> _serverBrowserList = new();
    public ReadOnlyObservableCollection<ServerBrowserEntry> ServerBrowserList { get; }

    private bool _isLoadingServers;
    public bool IsLoadingServers
    {
        get => _isLoadingServers;
        set => SetProperty(ref _isLoadingServers, value);
    }

    public bool HasServers => _serverBrowserList.Count > 0;

    // ========== 页面切换（三级：主页 / 大厅 / 服务器浏览器） ==========
    private bool _isShowingLobby;
    public bool IsShowingLobby
    {
        get => _isShowingLobby;
        set
        {
            if (SetProperty(ref _isShowingLobby, value))
            {
                OnPropertyChanged(nameof(BackButtonText));
                OnPropertyChanged(nameof(IsShowingServerBrowser));
                OnPropertyChanged(nameof(IsShowingMainPage));
            }
        }
    }

    private bool _isShowingServerBrowser;
    public bool IsShowingServerBrowser
    {
        get => _isShowingServerBrowser;
        set
        {
            if (SetProperty(ref _isShowingServerBrowser, value))
            {
                OnPropertyChanged(nameof(BackButtonText));
                OnPropertyChanged(nameof(IsShowingLobby));
                OnPropertyChanged(nameof(IsShowingMainPage));
            }
        }
    }

    public string BackButtonText => _isShowingLobby || _isShowingServerBrowser ? "← 返回" : "← 返回主页";

    /// <summary>是否显示主页面（非大厅且非服务器浏览器时）</summary>
    public bool IsShowingMainPage => !_isShowingLobby && !_isShowingServerBrowser;

    // ========== 适配器 ==========
    private WebSocketNetworkAdapter? _lobbyAdapter;
    private WebSocketNetworkAdapter? _serverBrowserAdapter;

    // ========== 命令 ==========
    public ICommand BackCommand { get; }
    public ICommand CreateRoomCommand { get; }
    public ICommand JoinRoomCommand { get; }
    public ICommand ShowLobbyCommand { get; }
    public ICommand RefreshRoomListCommand { get; }
    public ICommand JoinSelectedRoomCommand { get; }
    public ICommand BrowseServersCommand { get; }
    public ICommand RefreshServerListCommand { get; }
    public ICommand SelectServerCommand { get; }

    public NetworkSettingsViewModel(
        GameMode mode,
        Action<GameMode, INetworkAdapter?, string, string, int, string> navigate,
        Action backToMenu)
    {
        _mode = mode;
        _navigate = navigate;
        _backToMenu = backToMenu;

        // 加载持久化配置
        _config = GameConfig.Load();
        _serverUrl = _config.ServerUrl;
        _masterUrl = _config.MasterUrl;
        _ipAddress = _config.P2pIpAddress;
        _portText = _config.P2pPort.ToString();

        RoomList = new ReadOnlyObservableCollection<RoomListEntry>(_roomList);
        ServerBrowserList = new ReadOnlyObservableCollection<ServerBrowserEntry>(_serverBrowserList);

        BackCommand = new RelayCommand(_ => OnBack());
        CreateRoomCommand = new RelayCommand(_ => _ = OnConnect(isCreate: true), _ => !IsConnecting);
        JoinRoomCommand = new RelayCommand(_ => _ = OnConnect(isCreate: false), _ => !IsConnecting);
        ShowLobbyCommand = new RelayCommand(_ => _ = EnterLobby(), _ => IsServerMode && !IsConnecting);
        RefreshRoomListCommand = new RelayCommand(_ => _ = LoadRoomList(), _ => !IsLoadingRooms);
        JoinSelectedRoomCommand = new RelayCommand(param => JoinRoomByCode(param?.ToString() ?? ""), _ => IsServerMode);
        BrowseServersCommand = new RelayCommand(_ => _ = EnterServerBrowser(), _ => IsServerMode && !IsConnecting);
        RefreshServerListCommand = new RelayCommand(_ => _ = LoadServerList(), _ => !IsLoadingServers);
        SelectServerCommand = new RelayCommand(param => SelectServer(param?.ToString() ?? ""), _ => IsServerMode);
    }

    private async Task OnConnect(bool isCreate)
    {
        const string name = "玩家";

        if (_mode == GameMode.P2P)
            await ConnectP2P(name, isCreate);
        else
            await ConnectServer(name, isCreate);
    }

    private async Task ConnectP2P(string name, bool isHost)
    {
        IsConnecting = true;
        ConnectionStatus = "正在连接...";
        ConnectionStatusColor = "#f39c12";

        try
        {
            var port = int.TryParse(PortText, out var p) ? p : 9000;
            var adapter = new P2pNetworkAdapter(IpAddress, port, isHost);

            bool success = isHost
                ? await adapter.HostGameAsync("", name)
                : await adapter.JoinGameAsync("", name);

            if (success)
            {
                ConnectionStatus = isHost ? "房间已创建，等待其他玩家..." : "已加入房间！";
                ConnectionStatusColor = "#27ae60";
                _navigate(GameMode.P2P, adapter, name, IpAddress, port, "");
            }
            else
            {
                ConnectionStatus = "连接失败";
                ConnectionStatusColor = "#e74c3c";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"连接失败: {ex.Message}";
            ConnectionStatusColor = "#e74c3c";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task ConnectServer(string name, bool isCreate)
    {
        // 关闭大厅浏览连接，准备创建游戏连接
        _lobbyAdapter?.Dispose();
        _lobbyAdapter = null;

        IsConnecting = true;
        ConnectionStatus = "正在连接服务器...";
        ConnectionStatusColor = "#f39c12";

        try
        {
            // 加入房间时校验房间号
            if (!isCreate)
            {
                var code = string.IsNullOrWhiteSpace(RoomCode) ? "" : RoomCode.Trim().ToUpper();
                if (string.IsNullOrEmpty(code))
                {
                    ConnectionStatus = "请输入房间号";
                    ConnectionStatusColor = "#e74c3c";
                    IsConnecting = false;
                    return;
                }
                if (code.Length != 6 || !code.All(char.IsLetterOrDigit))
                {
                    ConnectionStatus = "房间号格式错误：应为6位字母或数字";
                    ConnectionStatusColor = "#e74c3c";
                    IsConnecting = false;
                    return;
                }
                RoomCode = code;
            }

            var adapter = new WebSocketNetworkAdapter(ServerUrl, name);
            await adapter.ConnectAsync();

            bool success;
            if (isCreate)
            {
                success = await adapter.CreateRoomAsync(IsPublicRoom);
                if (success)
                {
                    adapter.IsPublicRoom = IsPublicRoom;
                    ConnectionStatus = $"房间已创建: {adapter.RoomCode}";
                    ConnectionStatusColor = "#27ae60";
                }
            }
            else
            {
                success = await adapter.JoinRoomAsync(RoomCode);
                if (success)
                {
                    ConnectionStatus = $"已加入房间: {adapter.RoomCode}";
                    ConnectionStatusColor = "#27ae60";
                }
            }

            if (success)
            {
                _navigate(GameMode.Server, adapter, name, ServerUrl, 0, adapter.RoomCode);
            }
            else
            {
                ConnectionStatus = !string.IsNullOrEmpty(adapter.LastError)
                    ? $"连接失败: {adapter.LastError}"
                    : "连接失败: 服务器无响应";
                ConnectionStatusColor = "#e74c3c";
                await adapter.DisconnectAsync();
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"连接失败: {ex.Message}";
            ConnectionStatusColor = "#e74c3c";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    // ========== 页面导航 ==========

    private void OnBack()
    {
        if (_isShowingLobby)
        {
            IsShowingLobby = false;
        }
        else if (_isShowingServerBrowser)
        {
            IsShowingServerBrowser = false;
        }
        else
        {
            _backToMenu();
        }
    }

    private async Task EnterLobby()
    {
        IsShowingLobby = true;
        await LoadRoomList();
    }

    // ========== 大厅房间列表 ==========

    private async Task LoadRoomList()
    {
        if (_mode != GameMode.Server) return;
        IsLoadingRooms = true;
        try
        {
            if (_lobbyAdapter == null || !_lobbyAdapter.IsConnected)
            {
                _lobbyAdapter?.Dispose();
                _lobbyAdapter = new WebSocketNetworkAdapter(ServerUrl, "");
                _lobbyAdapter.OnRoomListUpdated += OnRoomListUpdated;
                await _lobbyAdapter.ConnectAsync();
            }
            await _lobbyAdapter.RequestRoomListAsync();
            _roomList.Clear();
            foreach (var entry in _lobbyAdapter.RoomList)
                _roomList.Add(entry);
            OnPropertyChanged(nameof(HasRooms));
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"加载房间列表失败: {ex.Message}";
            ConnectionStatusColor = "#e74c3c";
        }
        finally
        {
            IsLoadingRooms = false;
        }
    }

    private void JoinRoomByCode(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode)) return;
        RoomCode = roomCode.Trim().ToUpper();
        _ = OnConnect(isCreate: false);
    }

    private void OnRoomListUpdated()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _roomList.Clear();
            if (_lobbyAdapter != null)
            {
                foreach (var entry in _lobbyAdapter.RoomList)
                    _roomList.Add(entry);
            }
            OnPropertyChanged(nameof(HasRooms));
        });
    }

    // ========== 服务器浏览器 ==========

    private async Task EnterServerBrowser()
    {
        IsShowingServerBrowser = true;
        await LoadServerList();
    }

    private async Task LoadServerList()
    {
        IsLoadingServers = true;
        ConnectionStatus = "";
        try
        {
            // 创建独立连接到列表服务器
            _serverBrowserAdapter?.Dispose();
            _serverBrowserAdapter = new WebSocketNetworkAdapter(MasterUrl, "");
            _serverBrowserAdapter.OnServerListUpdated += OnServerListUpdated;
            await _serverBrowserAdapter.ConnectToLobbyAsync(MasterUrl);

            await _serverBrowserAdapter.RequestServerListAsync();
            _serverBrowserList.Clear();
            foreach (var entry in _serverBrowserAdapter.ServerBrowserList)
                _serverBrowserList.Add(entry);
            OnPropertyChanged(nameof(HasServers));

            if (_serverBrowserList.Count == 0)
            {
                ConnectionStatus = "列表服务器暂无可用服务器";
                ConnectionStatusColor = "#f39c12";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"连接列表服务器失败: {ex.Message}";
            ConnectionStatusColor = "#e74c3c";
        }
        finally
        {
            IsLoadingServers = false;
        }
    }

    private void SelectServer(string wsUrl)
    {
        if (string.IsNullOrWhiteSpace(wsUrl)) return;

        // 将选中的服务器地址填入服务器地址栏
        ServerUrl = wsUrl;
        IsShowingServerBrowser = false;
        ConnectionStatus = $"已选择服务器: {wsUrl}";
        ConnectionStatusColor = "#27ae60";
    }

    private void OnServerListUpdated()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _serverBrowserList.Clear();
            if (_serverBrowserAdapter != null)
            {
                foreach (var entry in _serverBrowserAdapter.ServerBrowserList)
                    _serverBrowserList.Add(entry);
            }
            OnPropertyChanged(nameof(HasServers));
        });
    }

    /// <summary>
    /// 保存当前设置到配置文件
    /// </summary>
    private void SaveConfig()
    {
        _config.ServerUrl = _serverUrl;
        _config.MasterUrl = _masterUrl;
        _config.P2pIpAddress = _ipAddress;
        if (int.TryParse(_portText, out int port))
            _config.P2pPort = port;
        _config.Save();
    }

    /// <summary>
    /// 清理资源（离开页面时调用）
    /// </summary>
    public void Cleanup()
    {
        if (_lobbyAdapter != null)
        {
            _lobbyAdapter.OnRoomListUpdated -= OnRoomListUpdated;
            _lobbyAdapter.Dispose();
        }
        _lobbyAdapter = null;

        if (_serverBrowserAdapter != null)
        {
            _serverBrowserAdapter.OnServerListUpdated -= OnServerListUpdated;
            _serverBrowserAdapter.Dispose();
        }
        _serverBrowserAdapter = null;
    }
}
