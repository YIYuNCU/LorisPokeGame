using System.Threading;
using System.Threading.Tasks;

namespace LolitaPoker.Core.Audio;

/// <summary>
/// One-shot sound effect service.
/// </summary>
public interface ISoundEffectService
{
    Task PlayAsync(string soundFileName, CancellationToken cancellationToken = default);
}
