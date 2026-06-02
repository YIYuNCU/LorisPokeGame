using System.Threading;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Audio;

/// <summary>
/// 空 BGM 实现：不播放任何音频。作为默认注入值。
/// </summary>
public sealed class NullBgmService : IBgmService
{
    public static readonly NullBgmService Instance = new();

    public bool IsPlaying => false;
    public double Volume { get; set; }

    public Task PlayAsync(string bgmFilePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Stop() { }
}
