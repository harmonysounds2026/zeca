using LAMG.Common;

namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Builds the ffmpeg filter graph for a planned mix and renders it to
/// the configured output format. Performs the atomic <c>.tmp -> final</c>
/// rename on success.
/// </summary>
public interface IMixRenderer
{
    Task<Result> RenderAsync(
        long mixId,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken = default);
}
