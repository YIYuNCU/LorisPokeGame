// -----------------------------------------------------------------------
// P2pNetworkAdapter.cs - P2P 网络适配器（占位实现）
// 实现 INetworkAdapter 接口，实际 P2P 网络逻辑待后续实现
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Network;

/// <summary>
/// P2P 局域网网络适配器（占位实现）
/// 目前仅记录操作日志，不执行实际网络通信
/// </summary>
public class P2pNetworkAdapter : INetworkAdapter
{
    public bool IsConnected { get; private set; }
    public string LocalPlayerId { get; } = Guid.NewGuid().ToString("N")[..8];

    public event Action<NetworkMessage>? OnMessageReceived;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<string, bool>? OnPlayerPresenceChanged;

    private readonly string _ipAddress;
    private readonly int _port;
    private readonly bool _isHost;

    public P2pNetworkAdapter(string ipAddress, int port, bool isHost)
    {
        _ipAddress = ipAddress;
        _port = port;
        _isHost = isHost;
    }

    public Task<bool> HostGameAsync(string roomId, string hostPlayerName)
    {
        Debug.WriteLine($"[P2P] 准备在 {_ipAddress}:{_port} 创建房间，玩家: {hostPlayerName}");
        IsConnected = true;
        OnConnectionStateChanged?.Invoke(true);
        return Task.FromResult(true);
    }

    public Task<bool> JoinGameAsync(string roomId, string playerName)
    {
        Debug.WriteLine($"[P2P] 准备加入 {_ipAddress}:{_port}，玩家: {playerName}");
        IsConnected = true;
        OnConnectionStateChanged?.Invoke(true);
        return Task.FromResult(true);
    }

    public Task SendMessageAsync(NetworkMessage message)
    {
        Debug.WriteLine($"[P2P] 发送消息: {message.Type} - {message.Payload}");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        Debug.WriteLine("[P2P] 断开连接");
        IsConnected = false;
        OnConnectionStateChanged?.Invoke(false);
        return Task.CompletedTask;
    }
}
