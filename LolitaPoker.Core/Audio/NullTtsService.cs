using System.Threading;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Audio;

/// <summary>
/// 空 TTS 实现：不播放语音，立即返回。作为默认注入值。
/// </summary>
public sealed class NullTtsService : ITtsService
{
    public static readonly NullTtsService Instance = new();

    public bool IsAvailable => false;

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
