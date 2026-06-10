using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using LolitaPoker.Core.Audio;

namespace LolitaPoker.App;

/// <summary>
/// 播放 WhiteVoice.mp3 模拟 TTS 效果。
/// </summary>
public sealed class WhiteVoiceTtsService : ITtsService, IDisposable
{
    private readonly MediaPlayer _player;
    private readonly string _audioPath;
    private TaskCompletionSource? _playTcs;

    public bool IsAvailable { get; }

    public WhiteVoiceTtsService()
    {
        _player = new MediaPlayer();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string primaryPath = Path.Combine(baseDir, "Audio", "Sfx", "WhiteVoice.mp3");
        string legacyPath = Path.Combine(baseDir, "WhiteVoice.mp3");
        _audioPath = File.Exists(primaryPath) ? primaryPath : legacyPath;
        IsAvailable = File.Exists(_audioPath);

        _player.MediaEnded += (_, _) =>
        {
            _playTcs?.TrySetResult();
        };

        _player.MediaFailed += (_, _) =>
        {
            System.Diagnostics.Trace.WriteLine("[TTS] MediaFailed");
            _playTcs?.TrySetResult();
        };

        System.Diagnostics.Trace.WriteLine($"[TTS] 路径: {_audioPath}, 可用: {IsAvailable}");
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return Task.CompletedTask;

        _playTcs?.TrySetResult();
        _playTcs = new TaskCompletionSource();

        cancellationToken.Register(() =>
        {
            _player.Stop();
            _playTcs?.TrySetCanceled();
        });

        _player.Open(new Uri(_audioPath, UriKind.Absolute));
        _player.Play();

        return _playTcs.Task;
    }

    public void Dispose()
    {
        _player.Stop();
        _player.Close();
    }
}
