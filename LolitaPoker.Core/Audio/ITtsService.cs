using System.Threading;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Audio;

/// <summary>
/// 文字转语音服务接口，允许外部注入具体实现。
/// </summary>
public interface ITtsService
{
    /// <summary>
    /// 将文本转为语音并播放，返回的 Task 在播放完成后完成。
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 当前 TTS 引擎是否可用。
    /// </summary>
    bool IsAvailable { get; }
}
