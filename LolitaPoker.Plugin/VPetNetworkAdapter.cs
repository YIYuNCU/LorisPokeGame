// -----------------------------------------------------------------------
// VPetNetworkAdapter.cs - VPet 访客表网络适配器
// 实现 INetworkAdapter，桥接 IMPWindows 消息收发
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using LolitaPoker.Core.Enums;
using LolitaPoker.Core.Network;
using VPet_Simulator.Windows.Interface;

namespace LolitaPoker.Plugin;

/// <summary>
/// VPet 访客表网络适配器
/// 通过 IMPWindows 的 Steam P2P 通道传输 LolitaPoker 游戏消息
/// </summary>
public class VPetNetworkAdapter : INetworkAdapter, IDisposable
{
    private readonly IMPWindows _mpWindows;
    private readonly ulong _localSteamId;
    private readonly bool _isHost;
    private readonly Dictionary<ulong, int> _seatMap = new();
    private readonly Dictionary<int, ulong> _reverseSeatMap = new();
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public string LocalPlayerId => _localSteamId.ToString();
    public bool IsHost => _isHost;

    /// <summary>座位映射（SteamID → 座位号）</summary>
    public IReadOnlyDictionary<ulong, int> SeatMap => _seatMap;

    /// <summary>反向座位映射（座位号 → SteamID）</summary>
    public IReadOnlyDictionary<int, ulong> ReverseSeatMap => _reverseSeatMap;

    public event Action<NetworkMessage>? OnMessageReceived;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<string, bool>? OnPlayerPresenceChanged;

    public VPetNetworkAdapter(IMPWindows mpWindows, ulong localSteamId, bool isHost)
    {
        _mpWindows = mpWindows ?? throw new ArgumentNullException(nameof(mpWindows));
        _localSteamId = localSteamId;
        _isHost = isHost;

        // 订阅访客表消息
        _mpWindows.ReceivedMessage += OnMpMessageReceived;
        _mpWindows.OnMemberJoined += OnMemberJoined;
        _mpWindows.OnMemberLeave += OnMemberLeave;
        _mpWindows.ClosingMutiPlayer += OnClosing;
    }

    /// <summary>
    /// 初始化座位映射（从访客表现有成员构建）
    /// </summary>
    public void InitializeSeatMap()
    {
        _seatMap.Clear();
        _reverseSeatMap.Clear();

        // 房主始终是座位 0
        _seatMap[_mpWindows.HostID] = 0;
        _reverseSeatMap[0] = _mpWindows.HostID;

        // 其他成员按加入顺序分配座位 1、2
        int nextSeat = 1;
        foreach (var friend in _mpWindows.Friends)
        {
            if (nextSeat >= 3) break;
            if (friend.FriendID == _mpWindows.HostID) continue;

            _seatMap[friend.FriendID] = nextSeat;
            _reverseSeatMap[nextSeat] = friend.FriendID;
            nextSeat++;
        }

        Debug.WriteLine($"[VPetNetworkAdapter] 座位映射初始化: {string.Join(", ", _seatMap.Select(kv => $"{kv.Key}→{kv.Value}"))}");
    }

    /// <summary>获取玩家座位号，不存在返回 -1</summary>
    public int GetSeat(ulong steamId)
    {
        return _seatMap.TryGetValue(steamId, out var seat) ? seat : -1;
    }

    /// <summary>获取座位对应的 SteamID，不存在返回 0</summary>
    public ulong GetSteamId(int seat)
    {
        return _reverseSeatMap.TryGetValue(seat, out var id) ? id : 0;
    }

    // ========== INetworkAdapter 实现 ==========

    public Task<bool> HostGameAsync(string roomId, string hostPlayerName)
    {
        InitializeSeatMap();
        IsConnected = true;
        OnConnectionStateChanged?.Invoke(true);
        Debug.WriteLine($"[VPetNetworkAdapter] 作为房主建立连接 (SteamID={_localSteamId})");
        return Task.FromResult(true);
    }

    public Task<bool> JoinGameAsync(string roomId, string playerName)
    {
        InitializeSeatMap();
        IsConnected = true;
        OnConnectionStateChanged?.Invoke(true);
        Debug.WriteLine($"[VPetNetworkAdapter] 作为成员加入 (SteamID={_localSteamId})");
        return Task.FromResult(true);
    }

    public Task SendMessageAsync(NetworkMessage message)
    {
        if (!IsConnected || _disposed) return Task.CompletedTask;

        var mpType = MapNetworkTypeToMpType(message.Type);
        if (mpType == 0)
        {
            Debug.WriteLine($"[VPetNetworkAdapter] 未知消息类型: {message.Type}，跳过");
            return Task.CompletedTask;
        }

        var mpMsg = new MPMessage { Type = mpType };

        // 将 JSON payload 转为 LPS 字符串
        if (!string.IsNullOrEmpty(message.Payload))
        {
            mpMsg.SetContent(message.Payload);
        }

        if (string.IsNullOrEmpty(message.TargetId))
        {
            // 广播
            _mpWindows.SendMessageALL(mpMsg);
        }
        else
        {
            // 定向发送
            if (ulong.TryParse(message.TargetId, out var targetSteamId))
            {
                _mpWindows.SendMessage(targetSteamId, mpMsg);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 发送带结构化载荷的定向消息（供 HostGameManager 使用）
    /// </summary>
    public void SendPayloadMessage(int mpType, object payload, ulong targetSteamId)
    {
        if (!IsConnected || _disposed) return;

        var mpMsg = new MPMessage { Type = mpType };
        mpMsg.SetContent(payload);
        _mpWindows.SendMessage(targetSteamId, mpMsg);
    }

    /// <summary>
    /// 发送带结构化载荷的广播消息（供 HostGameManager 使用）
    /// </summary>
    public void SendPayloadMessageAll(int mpType, object payload)
    {
        if (!IsConnected || _disposed) return;

        var mpMsg = new MPMessage { Type = mpType };
        mpMsg.SetContent(payload);
        _mpWindows.SendMessageALL(mpMsg);
    }

    /// <summary>
    /// 发送无载荷的广播消息
    /// </summary>
    public void SendMessageAll(int mpType)
    {
        if (!IsConnected || _disposed) return;

        var mpMsg = new MPMessage { Type = mpType };
        _mpWindows.SendMessageALL(mpMsg);
    }

    public Task DisconnectAsync()
    {
        if (_disposed) return Task.CompletedTask;

        IsConnected = false;
        OnConnectionStateChanged?.Invoke(false);
        Debug.WriteLine("[VPetNetworkAdapter] 已断开连接");
        return Task.CompletedTask;
    }

    // ========== IMPWindows 事件处理 ==========

    private void OnMpMessageReceived(ulong senderSteamId, MPMessage msg)
    {
        if (_disposed) return;

        // 只处理萝莉丝扑克的自定义消息类型
        if (msg.Type > -100 || msg.Type < -150) return;

        var networkType = MapMpTypeToNetworkType(msg.Type);
        if (string.IsNullOrEmpty(networkType)) return;

        string payload;

        // StartGame 特殊处理：提取 seed 和 firstPlayerIndex 转为 JSON
        if (msg.Type == VpetMpTypes.StartGame)
        {
            try
            {
                var startPayload = msg.GetContent<StartGamePayload>();
                payload = JsonSerializer.Serialize(new
                {
                    Seed = startPayload.Seed,
                    FirstPlayerIndex = startPayload.FirstPlayerIndex
                });

                // 存储玩家名供 GameViewModel 使用
                if (startPayload.PlayerNames != null)
                    _lastPlayerNames = startPayload.PlayerNames;
            }
            catch
            {
                payload = "{}";
            }
        }
        else
        {
            payload = msg.GetContent();
        }

        var networkMsg = new NetworkMessage
        {
            Type = networkType,
            SenderId = senderSteamId.ToString(),
            Payload = payload,
            Timestamp = DateTime.UtcNow
        };

        OnMessageReceived?.Invoke(networkMsg);
    }

    /// <summary>最后一次收到的玩家名（供 GameViewModel 初始化用）</summary>
    private string[]? _lastPlayerNames;
    public string[]? LastPlayerNames => _lastPlayerNames;

    private void OnMemberJoined(ulong steamId)
    {
        if (_disposed) return;

        // 游戏进行中不接受新成员
        if (_seatMap.Count >= 3) return;

        if (!_seatMap.ContainsKey(steamId))
        {
            int nextSeat = _seatMap.Count;
            if (nextSeat < 3)
            {
                _seatMap[steamId] = nextSeat;
                _reverseSeatMap[nextSeat] = steamId;
                Debug.WriteLine($"[VPetNetworkAdapter] 玩家加入: {steamId} → 座位 {nextSeat}");
                OnPlayerPresenceChanged?.Invoke(steamId.ToString(), true);
            }
        }
    }

    private void OnMemberLeave(ulong steamId)
    {
        if (_disposed) return;

        if (_seatMap.TryGetValue(steamId, out var seat))
        {
            // 不立即移除座位映射，避免游戏中断
            // 仅通知上层玩家离开
            Debug.WriteLine($"[VPetNetworkAdapter] 玩家离开: {steamId} (座位 {seat})");
            OnPlayerPresenceChanged?.Invoke(steamId.ToString(), false);
        }
    }

    private void OnClosing()
    {
        if (_disposed) return;

        IsConnected = false;
        OnConnectionStateChanged?.Invoke(false);
        Debug.WriteLine("[VPetNetworkAdapter] 访客表关闭");
    }

    // ========== 类型映射 ==========

    private static int MapNetworkTypeToMpType(string networkType)
    {
        return networkType switch
        {
            "ready" => VpetMpTypes.GameReady,
            "cancel_ready" => VpetMpTypes.GameUnready,
            "bid" => VpetMpTypes.Bid,
            "play" => VpetMpTypes.PlayCards,
            "pass" => VpetMpTypes.Pass,
            "new_game" => VpetMpTypes.StartGame,
            _ => 0
        };
    }

    private static string MapMpTypeToNetworkType(int mpType)
    {
        return mpType switch
        {
            VpetMpTypes.GameReady => "ready",
            VpetMpTypes.GameUnready => "cancel_ready",
            VpetMpTypes.Bid => "bid",
            VpetMpTypes.PlayCards => "play",
            VpetMpTypes.Pass => "pass",
            VpetMpTypes.StartGame => "new_game",
            _ => ""
        };
    }

    // ========== IDisposable ==========

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _mpWindows.ReceivedMessage -= OnMpMessageReceived;
        _mpWindows.OnMemberJoined -= OnMemberJoined;
        _mpWindows.OnMemberLeave -= OnMemberLeave;
        _mpWindows.ClosingMutiPlayer -= OnClosing;

        IsConnected = false;
        Debug.WriteLine("[VPetNetworkAdapter] 已释放资源");
    }
}
