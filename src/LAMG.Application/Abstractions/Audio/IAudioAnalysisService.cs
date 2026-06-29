using LAMG.Common;
using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Composite service that runs the full per-track analysis pipeline
/// (probe + file hash + audio hash + silence + loudness) and returns
/// a fully populated <see cref="Track"/> (still in <c>Pending</c>
/// status until the caller decides). Wraps any per-step failure into
/// a <see cref="Result{T}"/> so the job pipeline can skip-and-continue.
/// </summary>
public interface IAudioAnalysisService
{
    Task<Result<Track>> AnalyzeAsync(
        string filePath,
        long batchId,
        CancellationToken cancellationToken = default);
}
