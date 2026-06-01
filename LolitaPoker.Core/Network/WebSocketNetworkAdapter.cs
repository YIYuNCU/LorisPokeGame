// -----------------------------------------------------------------------
// WebSocketNetworkAdapter.cs - WebSocket 网络适配器
// 连接 FastAPI 服务器，实现 INetworkAdapter 接口
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Network;

/// <summary>大厅中的玩家信息</summary>
public class LobbyPlayerInfo
{
    public int Seat { get; set; }
    public string Name { get; set; } = "";
    public bool Ready { get; set; }
}

/// <summary>房间列表条目</summary>
public class RoomListEntry
{
    public string RoomCode { get; set; } = "";
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public List<string> Players { get; set; } = new();

    /// <summary>玩家名列表的显示文本</summary>
    public string PlayersText => Players.Count > 0
        ? "玩家: " + string.Join("、", Players)
        : "等待加入...";
}

/// <summary>
/// WebSocket 网络适配器 - 连接远程 FastAPI 游戏服务器
/// </summary>
public class WebSocketNetworkAdapter : INetworkAdapter, IDisposable
{
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private TaskCompletionSource<JsonElement>? _pendingResponse;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;
    public string LocalPlayerId { get; private set; } = "";
    public string RoomCode { get; private set; } = "";
    public int AssignedSeat { get; private set; } = -1;
    public string LastError { get; private set; } = "";

    /// <summary>大厅玩家列表（加入房间时从服务端获取）</summary>
    public List<LobbyPlayerInfo> LobbyPlayers { get; private set; } = new();

    /// <summary>公共房间列表（用于大厅浏览）</summary>
    public List<RoomListEntry> RoomList { get; private set; } = new();

    /// <summary>房间可见性（创建时设置）</summary>
    public bool IsPublicRoom { get; set; } = true;

    public event Action<NetworkMessage>? OnMessageReceived;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<string, bool>? OnPlayerPresenceChanged;
    public event Action? OnRoomListUpdated;

    private readonly string _serverUrl;
    private readonly string _playerName;
    private string _reconnectPlayerId = "";  // 用于断线重连
    private bool _isReconnecting;
    private bool _disconnectRequested;  // 主动断开时不重连

    public WebSocketNetworkAdapter(string serverUrl, string playerName)
    {
        _serverUrl = serverUrl;
        _playerName = playerName;
    }

    /// <summary>
    /// 连接到服务器
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _webSocket = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await _webSocket.ConnectAsync(new Uri(_serverUrl), _cts.Token);
        OnConnectionStateChanged?.Invoke(true);

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// 创建房间
    /// </summary>
    public async Task<bool> CreateRoomAsync(bool isPublic = true, CancellationToken ct = default)
    {
        // 先设好等待再发消息，避免本机回环时响应先到被丢弃
        _pendingResponse = new TaskCompletionSource<JsonElement>();
        await SendJsonAsync(new { type = "create_room", payload = new { player_name = _playerName, is_public = isPublic } }, ct);

        var response = await WaitForResponseAsync(ct);
        if (response.ValueKind == JsonValueKind.Undefined) return false;

        var type = response.GetProperty("type").GetString();
        if (type == "room_created")
        {
            var payload = response.GetProperty("payload");
            RoomCode = payload.GetProperty("room_code").GetString() ?? "";
            LocalPlayerId = payload.GetProperty("player_id").GetString() ?? "";
            AssignedSeat = payload.GetProperty("seat").GetInt32();
            _reconnectPlayerId = LocalPlayerId;

            // 初始化大厅列表（创建者自己）
            LobbyPlayers.Clear();
            LobbyPlayers.Add(new LobbyPlayerInfo
            {
                Seat = AssignedSeat,
                Name = _playerName,
                Ready = false
            });

            return true;
        }

        return false;
    }

    /// <summary>
    /// 加入房间
    /// </summary>
    public async Task<bool> JoinRoomAsync(string roomCode, CancellationToken ct = default)
    {
        // 先设好等待再发消息，避免本机回环时响应先到被丢弃
        _pendingResponse = new TaskCompletionSource<JsonElement>();
        await SendJsonAsync(new { type = "join_room", payload = new { room_code = roomCode, player_name = _playerName } }, ct);

        var response = await WaitForResponseAsync(ct);
        if (response.ValueKind == JsonValueKind.Undefined) return false;

        var type = response.GetProperty("type").GetString();
        if (type == "room_joined")
        {
            var payload = response.GetProperty("payload");
            RoomCode = payload.GetProperty("room_code").GetString() ?? "";
            LocalPlayerId = payload.GetProperty("player_id").GetString() ?? "";
            AssignedSeat = payload.GetProperty("seat").GetInt32();
            _reconnectPlayerId = LocalPlayerId;
            ParseLobbyPlayers(payload);
            return true;
        }

        if (type == "error")
        {
            var msg = response.GetProperty("payload").GetProperty("message").GetString() ?? "未知错误";
            LastError = msg;
            Trace.WriteLine($"[WS] 加入房间失败: {msg}");
        }

        return false;
    }

    /// <summary>
    /// 请求房间列表
    /// </summary>
    public async Task RequestRoomListAsync(CancellationToken ct = default)
    {
        _pendingResponse = new TaskCompletionSource<JsonElement>();
        await SendJsonAsync(new { type = "list_rooms", payload = new { } }, ct);

        var response = await WaitForResponseAsync(ct);
        if (response.ValueKind == JsonValueKind.Undefined) return;

        var type = response.GetProperty("type").GetString();
        if (type == "room_list")
        {
            ParseRoomList(response.GetProperty("payload"));
        }
    }

    /// <summary>
    /// 设置房间公开/私密
    /// </summary>
    public async Task SetRoomVisibilityAsync(bool isPublic, CancellationToken ct = default)
    {
        await SendJsonAsync(new { type = "set_room_visibility", payload = new { is_public = isPublic } }, ct);
    }

    /// <summary>
    /// 发送重连投票
    /// </summary>
    public async Task SendReconnectVoteAsync(string choice, CancellationToken ct = default)
    {
        await SendJsonAsync(new { type = "reconnect_vote", payload = new { choice } }, ct);
    }

    /// <summary>
    /// 尝试断线重连（内部方法，由 ReceiveLoopAsync 在断线时调用）
    /// </summary>
    private async Task<bool> TryReconnectAsync()
    {
        if (string.IsNullOrEmpty(_reconnectPlayerId) || _isReconnecting)
            return false;

        _isReconnecting = true;
        const int maxAttempts = 6;
        const int delayMs = 10000;

        try
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Trace.WriteLine($"[WS] 重连尝试 {attempt + 1}/{maxAttempts}...");

                try
                {
                    _webSocket?.Dispose();
                    _webSocket = new ClientWebSocket();
                    _cts = new CancellationTokenSource();

                    var uri = new Uri($"{_serverUrl}?reconnect_player_id={_reconnectPlayerId}");
                    await _webSocket.ConnectAsync(uri, _cts.Token);

                    if (_webSocket.State == WebSocketState.Open)
                    {
                        Trace.WriteLine("[WS] 重连成功");
                        OnConnectionStateChanged?.Invoke(true);
                        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[WS] 重连尝试失败: {ex.Message}");
                }

                await Task.Delay(delayMs);
            }
        }
        finally
        {
            _isReconnecting = false;
        }

        Trace.WriteLine("[WS] 重连失败，已用尽所有尝试");
        return false;
    }

    // ========== INetworkAdapter 实现 ==========

    public async Task<bool> HostGameAsync(string roomId, string hostPlayerName)
    {
        try
        {
            if (!IsConnected)
                await ConnectAsync();
            return await CreateRoomAsync();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WS] 创建房间异常: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> JoinGameAsync(string roomId, string playerName)
    {
        try
        {
            if (!IsConnected)
                await ConnectAsync();
            return await JoinRoomAsync(roomId);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WS] 加入房间异常: {ex.Message}");
            return false;
        }
    }

    public async Task SendMessageAsync(NetworkMessage message)
    {
        if (!IsConnected) return;

        try
        {
            Trace.WriteLine($"[WS] 发送消息: {message.Type} {message.Payload}");
            await SendJsonAsync(new { type = message.Type, payload = JsonSerializer.Deserialize<object>(message.Payload) });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WS] 发送消息异常: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        _disconnectRequested = true;
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "客户端断开", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WS] 断开连接异常: {ex.Message}");
        }
        finally
        {
            _cts?.Cancel();
            OnConnectionStateChanged?.Invoke(false);
        }
    }

    // ========== 内部方法 ==========

    private async Task SendJsonAsync(object data, CancellationToken ct = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket 未连接");

        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<JsonElement> WaitForResponseAsync(CancellationToken ct = default)
    {
        if (_pendingResponse == null)
            return default;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            cts.Token.Register(() => _pendingResponse.TrySetResult(default));
            return await _pendingResponse.Task;
        }
        catch
        {
            return default;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnConnectionStateChanged?.Invoke(false);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                var json = sb.ToString();

                try
                {
                    var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var type = root.GetProperty("type").GetString() ?? "";

                    Trace.WriteLine($"[WS] 收到消息: {type}");

                    // 仅当消息是真正的请求响应时才传递给 _pendingResponse
                    // 通知类消息（player_joined, player_ready 等）始终走 DispatchMessage
                    bool isResponse = type is "room_created" or "room_joined" or "error" or "room_list";
                    if (isResponse && _pendingResponse != null && !_pendingResponse.Task.IsCompleted)
                    {
                        _pendingResponse.TrySetResult(root);
                        continue;
                    }

                    // 通知消息分发
                    DispatchMessage(type, root);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[WS] 解析消息异常: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            Trace.WriteLine($"[WS] 连接异常断开: {ex.Message}");
        }
        finally
        {
            // 主动断开或无重连ID → 不尝试重连
            if (_disconnectRequested || string.IsNullOrEmpty(_reconnectPlayerId) || _isReconnecting)
            {
                OnConnectionStateChanged?.Invoke(false);
            }
            else
            {
                // 异常断线且有重连ID → 尝试自动重连
                _ = Task.Run(async () =>
                {
                    bool reconnected = await TryReconnectAsync();
                    if (!reconnected)
                    {
                        OnConnectionStateChanged?.Invoke(false);
                    }
                });
            }
        }
    }

    private void ParseLobbyPlayers(System.Text.Json.JsonElement payload)
    {
        if (!payload.TryGetProperty("players", out var arr)) return;
        LobbyPlayers.Clear();
        foreach (var el in arr.EnumerateArray())
        {
            LobbyPlayers.Add(new LobbyPlayerInfo
            {
                Seat = el.GetProperty("seat").GetInt32(),
                Name = el.GetProperty("name").GetString() ?? "",
                Ready = el.GetProperty("ready").GetBoolean(),
            });
        }
    }

    private void ParseRoomList(System.Text.Json.JsonElement payload)
    {
        if (!payload.TryGetProperty("rooms", out var arr)) return;
        RoomList.Clear();
        foreach (var el in arr.EnumerateArray())
        {
            var entry = new RoomListEntry
            {
                RoomCode = el.GetProperty("room_code").GetString() ?? "",
                PlayerCount = el.GetProperty("player_count").GetInt32(),
                MaxPlayers = el.GetProperty("max_players").GetInt32(),
            };
            if (el.TryGetProperty("players", out var playersArr))
            {
                foreach (var p in playersArr.EnumerateArray())
                    entry.Players.Add(p.GetString() ?? "");
            }
            RoomList.Add(entry);
        }
    }

    private void DispatchMessage(string type, JsonElement root)
    {
        var payload = root.TryGetProperty("payload", out var p) ? p : default;

        switch (type)
        {
            case "player_joined":
            {
                var pid = payload.GetProperty("player_id").GetString() ?? "";
                var pname = payload.GetProperty("player_name").GetString() ?? "";
                var pseat = payload.GetProperty("seat").GetInt32();
                ParseLobbyPlayers(payload);
                OnPlayerPresenceChanged?.Invoke(pid, true);
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "PlayerJoin",
                    Payload = JsonSerializer.Serialize(new { playerId = pid, playerName = pname, seat = pseat })
                });
                break;
            }
            case "player_left":
            {
                var pid = payload.GetProperty("player_id").GetString() ?? "";
                ParseLobbyPlayers(payload);
                OnPlayerPresenceChanged?.Invoke(pid, false);
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "PlayerLeave",
                    Payload = JsonSerializer.Serialize(new { playerId = pid })
                });
                break;
            }
            case "game_start":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "GameStart",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "player_ready":
            {
                int readyCount = payload.GetProperty("ready_count").GetInt32();
                int totalNeeded = payload.GetProperty("total_needed").GetInt32();
                ParseLobbyPlayers(payload);
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "PlayerReady",
                    Payload = JsonSerializer.Serialize(new { readyCount, totalNeeded })
                });
                break;
            }
            case "bid_update":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "BidUpdate",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "landlord_assigned":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "LandlordAssigned",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "turn_change":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "TurnChange",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "cards_played":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "CardsPlayed",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "player_passed":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "PlayerPassed",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "game_restart":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "GameRestart",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "game_over":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "GameOver",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "reconnected":
            {
                // 重连成功：更新本地状态
                if (payload.TryGetProperty("player_id", out var pid))
                    LocalPlayerId = pid.GetString() ?? "";
                if (payload.TryGetProperty("seat", out var seat))
                    AssignedSeat = seat.GetInt32();
                if (payload.TryGetProperty("room_code", out var rc))
                    RoomCode = rc.GetString() ?? "";

                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "Reconnected",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "room_list_updated":
            {
                ParseRoomList(payload);
                OnRoomListUpdated?.Invoke();
                break;
            }
            case "vote_start":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "VoteStart",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "vote_update":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "VoteUpdate",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "reconnect_waiting":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "ReconnectWaiting",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "player_reconnected":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "PlayerReconnected",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "game_ended":
            {
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "GameEnded",
                    Payload = payload.GetRawText()
                });
                break;
            }
            case "error":
            {
                var msg = payload.TryGetProperty("message", out var m) ? m.GetString() : "未知错误";
                Trace.WriteLine($"[WS] 服务器错误: {msg}");
                OnMessageReceived?.Invoke(new NetworkMessage
                {
                    Type = "Error",
                    Payload = JsonSerializer.Serialize(new { message = msg })
                });
                break;
            }
            default:
                Trace.WriteLine($"[WS] 未处理的消息类型: {type}");
                break;
        }
    }

    public void Dispose()
    {
        _disconnectRequested = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _webSocket?.Dispose();
    }
}
