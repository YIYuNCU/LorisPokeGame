using System.Threading;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Audio;

/// <summary>
/// 背景音乐服务接口，允许外部注入具体实现。
/// </summary>
public interface IBgmService
{
    /// <summary>
    /// 播放指定 BGM 文件（循环播放），如果已在播放相同文件则忽略。
    /// </summary>
    Task PlayAsync(string bgmFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止当前 BGM 播放。
    /// </summary>
    void Stop();

    /// <summary>
    /// 音量（0.0 ~ 1.0）。
    /// </summary>
    double Volume { get; set; }

    /// <summary>
    /// 是否正在播放。
    /// </summary>
    bool IsPlaying { get; }
}
