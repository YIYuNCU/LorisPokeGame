// -----------------------------------------------------------------------
// INetworkAdapter.cs - 网络适配器接口
// 底层传输抽象，可替换实现（TCP/UDP/WebSocket/SignalR等）
// -----------------------------------------------------------------------

using System.Threading.Tasks;

namespace LolitaPoker.Core.Network;

/// <summary>
/// 网络消息，所有网络通信的基本单元
/// </summary>
public class NetworkMessage
{
    /// <summary>消息类型</summary>
    public string Type { get; set; } = "";
    /// <summary>发送者ID</summary>
    public string SenderId { get; set; } = "";
    /// <summary>目标ID（空字符串=广播）</summary>
    public string TargetId { get; set; } = "";
    /// <summary>JSON序列化的负载数据</summary>
    public string Payload { get; set; } = "";
    /// <summary>消息时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 网络适配器接口 - 抽象底层传输协议
/// </summary>
public interface INetworkAdapter
{
    /// <summary>是否已连接</summary>
    bool IsConnected { get; }

    /// <summary>本机玩家ID</summary>
    string LocalPlayerId { get; }

    /// <summary>接收到消息时触发</summary>
    event Action<NetworkMessage> OnMessageReceived;

    /// <summary>连接状态变化时触发</summary>
    event Action<bool> OnConnectionStateChanged;

    /// <summary>玩家加入/离开时触发（玩家ID, 是否加入）</summary>
    event Action<string, bool> OnPlayerPresenceChanged;

    /// <summary>
    /// 作为主机创建游戏房间
    /// </summary>
    Task<bool> HostGameAsync(string roomId, string hostPlayerName);

    /// <summary>
    /// 加入已有的游戏房间
    /// </summary>
    Task<bool> JoinGameAsync(string roomId, string playerName);

    /// <summary>
    /// 发送消息（广播或定向）
    /// </summary>
    Task SendMessageAsync(NetworkMessage message);

    /// <summary>
    /// 断开连接
    /// </summary>
    Task DisconnectAsync();
}
