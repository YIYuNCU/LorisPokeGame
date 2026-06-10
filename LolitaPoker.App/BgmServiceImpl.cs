using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using LolitaPoker.Core.Audio;

namespace LolitaPoker.App;

/// <summary>
/// 基于 MediaPlayer 的 BGM 实现，循环播放 Audio/Bgm 目录中的音频。
/// </summary>
public sealed class BgmServiceImpl : IBgmService, IDisposable
{
    private static readonly string[] SupportedExtensions = [".mp3", ".wav"];

    private readonly MediaPlayer _player;
    private readonly List<string> _playlist;
    private int _currentIndex;
    private bool _playing;
    private bool _pendingPlay;

    public bool IsPlaying => _playing;
    public double Volume { get; set; } = 0.5;

    public BgmServiceImpl()
    {
        _player = new MediaPlayer();
        _playlist = LoadPlaylist();

        _player.Volume = Volume;
        _player.MediaEnded += (_, _) =>
        {
            PlayNext();
        };

        _player.MediaOpened += (_, _) =>
        {
            System.Diagnostics.Trace.WriteLine("[BGM] MediaOpened");
            if (_pendingPlay)
            {
                _pendingPlay = false;
                _player.Volume = Volume;
                _player.Play();
                _playing = true;
                System.Diagnostics.Trace.WriteLine("[BGM] Play from MediaOpened");
            }
        };

        _player.MediaFailed += (_, e) =>
        {
            System.Diagnostics.Trace.WriteLine($"[BGM] MediaFailed: {e.ErrorException.Message}");
        };

        System.Diagnostics.Trace.WriteLine($"[BGM] 曲目数量: {_playlist.Count}");
    }

    public Task PlayAsync(string bgmFilePath, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(bgmFilePath);
        if (!File.Exists(path))
        {
            System.Diagnostics.Trace.WriteLine($"[BGM] 文件不存在: {path}");
            return Task.CompletedTask;
        }

        try
        {
            _pendingPlay = true;
            _player.Open(new Uri(path, UriKind.Absolute));
            System.Diagnostics.Trace.WriteLine($"[BGM] Open: {path}, 等待 MediaOpened");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[BGM] 播放失败: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private static List<string> LoadPlaylist()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string bgmDir = Path.Combine(baseDir, "Audio", "Bgm");
        var files = Directory.Exists(bgmDir)
            ? Directory.EnumerateFiles(bgmDir)
                .Where(IsSupportedAudioFile)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        if (files.Count == 0)
        {
            string legacyPath = Path.Combine(baseDir, "Background.mp3");
            if (File.Exists(legacyPath))
                files.Add(legacyPath);
        }

        return files;
    }

    private static bool IsSupportedAudioFile(string path)
        => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private string ResolvePath(string bgmFilePath)
    {
        if (!string.IsNullOrWhiteSpace(bgmFilePath) && File.Exists(bgmFilePath))
            return bgmFilePath;

        return _playlist.Count > 0 ? _playlist[_currentIndex] : string.Empty;
    }

    private void PlayNext()
    {
        if (_playlist.Count == 0)
        {
            _playing = false;
            return;
        }

        _currentIndex = (_currentIndex + 1) % _playlist.Count;
        _pendingPlay = true;
        _player.Open(new Uri(_playlist[_currentIndex], UriKind.Absolute));
    }

    public void Stop()
    {
        _player.Stop();
        _playing = false;
    }

    public void Dispose()
    {
        _player.Stop();
        _player.Close();
    }
}
