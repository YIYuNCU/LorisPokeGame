using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Xunit;
using LolitaPoker.Core;

namespace LolitaPoker.Tests;

public class GameConfigTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        // Reset the static _configFilePath to avoid polluting other tests
        ResetConfigFilePath();

        // Clean up temp files
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"game_config_test_{Guid.NewGuid():N}.json");
        _tempFiles.Add(path);
        return path;
    }

    private static void ResetConfigFilePath()
    {
        var field = typeof(GameConfig).GetField("_configFilePath",
            BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }

    private static void SetConfigFilePath(string path)
    {
        var field = typeof(GameConfig).GetField("_configFilePath",
            BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, path);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var config = new GameConfig();

        Assert.Equal("ws://127.0.0.1:8000/ws", config.ServerUrl);
        Assert.Equal("127.0.0.1", config.P2pIpAddress);
        Assert.Equal(9000, config.P2pPort);
    }

    [Fact]
    public void SaveTo_LoadFrom_RoundTrip()
    {
        var path = CreateTempFile();

        var original = new GameConfig
        {
            ServerUrl = "ws://192.168.1.100:9090/ws",
            P2pIpAddress = "192.168.1.200",
            P2pPort = 12345
        };

        original.SaveTo(path);
        var loaded = GameConfig.LoadFrom(path);

        Assert.Equal(original.ServerUrl, loaded.ServerUrl);
        Assert.Equal(original.P2pIpAddress, loaded.P2pIpAddress);
        Assert.Equal(original.P2pPort, loaded.P2pPort);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        // Set _configFilePath to a path that does not exist
        SetConfigFilePath(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}", "game_config.json"));

        var config = GameConfig.Load();

        Assert.Equal("ws://127.0.0.1:8000/ws", config.ServerUrl);
        Assert.Equal("127.0.0.1", config.P2pIpAddress);
        Assert.Equal(9000, config.P2pPort);
    }

    [Fact]
    public void LoadFrom_MissingFile_ThrowsException()
    {
        // Use a path with an existing directory but nonexistent file to get FileNotFoundException
        var fakePath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json");

        Assert.Throws<FileNotFoundException>(() => GameConfig.LoadFrom(fakePath));
    }

    [Fact]
    public void JsonPropertyNames_AreCorrect()
    {
        var path = CreateTempFile();

        var config = new GameConfig
        {
            ServerUrl = "ws://test:1234/ws",
            P2pIpAddress = "10.0.0.1",
            P2pPort = 5555
        };

        config.SaveTo(path);
        var rawJson = File.ReadAllText(path);
        var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("server_url", out _));
        Assert.True(root.TryGetProperty("p2p_ip", out _));
        Assert.True(root.TryGetProperty("p2p_port", out _));

        Assert.Equal("ws://test:1234/ws", root.GetProperty("server_url").GetString());
        Assert.Equal("10.0.0.1", root.GetProperty("p2p_ip").GetString());
        Assert.Equal(5555, root.GetProperty("p2p_port").GetInt32());
    }
}
