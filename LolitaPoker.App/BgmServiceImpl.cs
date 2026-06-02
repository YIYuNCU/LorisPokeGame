using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using LolitaPoker.Core.Audio;

namespace LolitaPoker.App;

/// <summary>
/// 基于 MediaPlayer 的 BGM 实现，循环播放 Background.mp3。
/// </summary>
public sealed class BgmServiceImpl : IBgmService, IDisposable
{
    private readonly MediaPlayer _player;
    private readonly string _audioPath;
    private bool _playing;
    private bool _pendingPlay;

    public bool IsPlaying => _playing;
    public double Volume { get; set; } = 0.5;

    public BgmServiceImpl()
    {
        _player = new MediaPlayer();
        _audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Background.mp3");

        _player.Volume = Volume;
        _player.MediaEnded += (_, _) =>
        {
            // 循环播放
            _player.Position = TimeSpan.Zero;
            _player.Play();
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

        System.Diagnostics.Trace.WriteLine($"[BGM] 路径: {_audioPath}, 存在: {File.Exists(_audioPath)}");
    }

    public Task PlayAsync(string bgmFilePath, CancellationToken cancellationToken = default)
    {
        string path = File.Exists(bgmFilePath) ? bgmFilePath : _audioPath;
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
