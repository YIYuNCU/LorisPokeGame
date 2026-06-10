using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using LolitaPoker.Core.Audio;

namespace LolitaPoker.App;

/// <summary>
/// MediaPlayer-backed one-shot sound effects loaded from Audio/Sfx.
/// </summary>
public sealed class SoundEffectServiceImpl : ISoundEffectService, IDisposable
{
    private readonly List<MediaPlayer> _activePlayers = new();
    private readonly object _lock = new();
    private readonly string _sfxDirectory;

    public double Volume { get; set; } = 0.8;

    public SoundEffectServiceImpl()
    {
        _sfxDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio", "Sfx");
        System.Diagnostics.Trace.WriteLine($"[SFX] 路径: {_sfxDirectory}, 存在: {Directory.Exists(_sfxDirectory)}");
    }

    public Task PlayAsync(string soundFileName, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(soundFileName);
        if (!File.Exists(path))
        {
            System.Diagnostics.Trace.WriteLine($"[SFX] 文件不存在，跳过: {soundFileName}");
            return Task.CompletedTask;
        }

        try
        {
            var player = new MediaPlayer { Volume = Volume };
            player.MediaEnded += (_, _) => CleanupPlayer(player);
            player.MediaFailed += (_, e) =>
            {
                System.Diagnostics.Trace.WriteLine($"[SFX] MediaFailed: {e.ErrorException.Message}");
                CleanupPlayer(player);
            };

            lock (_lock)
                _activePlayers.Add(player);

            cancellationToken.Register(() => CleanupPlayer(player));
            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[SFX] 播放失败: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string soundFileName)
    {
        if (string.IsNullOrWhiteSpace(soundFileName))
            return string.Empty;

        if (Path.IsPathFullyQualified(soundFileName))
            return soundFileName;

        string sfxPath = Path.Combine(_sfxDirectory, soundFileName);
        if (File.Exists(sfxPath))
            return sfxPath;

        // 兼容旧部署结构：WhiteVoice.mp3 曾位于程序根目录。
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, soundFileName);
    }

    private void CleanupPlayer(MediaPlayer player)
    {
        lock (_lock)
            _activePlayers.Remove(player);

        player.Stop();
        player.Close();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var player in _activePlayers.ToArray())
            {
                player.Stop();
                player.Close();
            }
            _activePlayers.Clear();
        }
    }
}
