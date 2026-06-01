// -----------------------------------------------------------------------
// GameConfig.cs - 游戏配置持久化管理
// 将配置保存为 JSON 文件（game_config.json），支持外部读写
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LolitaPoker.Core;

/// <summary>
/// 游戏配置管理器，支持持久化到 JSON 文件
/// 配置文件位于可执行文件同目录下，可被外部程序读写
/// </summary>
public class GameConfig
{
    private static readonly string ConfigFileName = "game_config.json";

    private static string? _configFilePath;

    /// <summary>
    /// 配置文件的完整路径（只读属性，供外部工具查找配置文件）
    /// </summary>
    public static string ConfigFilePath
    {
        get
        {
            if (_configFilePath == null)
            {
                // 优先 BaseDirectory（debug 时 Environment.ProcessPath 指向 dotnet.exe）
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                _configFilePath = Path.Combine(exeDir, ConfigFileName);
            }
            return _configFilePath;
        }
    }

    /// <summary>
    /// 服务器 WebSocket 地址
    /// </summary>
    [JsonPropertyName("server_url")]
    public string ServerUrl { get; set; } = "ws://127.0.0.1:8000/ws";

    /// <summary>
    /// P2P 模式 IP 地址
    /// </summary>
    [JsonPropertyName("p2p_ip")]
    public string P2pIpAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// P2P 模式端口
    /// </summary>
    [JsonPropertyName("p2p_port")]
    public int P2pPort { get; set; } = 9000;

    /// <summary>
    /// 列表服务器（主服务器）WebSocket 地址
    /// </summary>
    [JsonPropertyName("master_url")]
    public string MasterUrl { get; set; } = "ws://127.0.0.1:8000/ws/lobby";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 从配置文件加载配置，文件不存在时返回默认配置
    /// </summary>
    public static GameConfig Load()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var config = JsonSerializer.Deserialize<GameConfig>(json, JsonOptions);
                if (config != null)
                    return config;
            }
        }
        catch (Exception)
        {
            // 读取失败时返回默认配置
        }
        return new GameConfig();
    }

    /// <summary>
    /// 将当前配置保存到文件
    /// </summary>
    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception)
        {
            // 保存失败时静默忽略
        }
    }

    /// <summary>
    /// 从外部 JSON 文件路径加载配置（供外部工具使用）
    /// </summary>
    public static GameConfig LoadFrom(string filePath)
    {
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<GameConfig>(json, JsonOptions) ?? new GameConfig();
    }

    /// <summary>
    /// 将配置保存到指定路径（供外部工具使用）
    /// </summary>
    public void SaveTo(string filePath)
    {
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(filePath, json);
    }
}
