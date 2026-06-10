using System.Threading;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Audio;

/// <summary>
/// Sound-effect service used when no audio backend is available.
/// </summary>
public sealed class NullSoundEffectService : ISoundEffectService
{
    public static readonly NullSoundEffectService Instance = new();

    private NullSoundEffectService() { }

    public Task PlayAsync(string soundFileName, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
