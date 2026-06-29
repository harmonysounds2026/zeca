using LAMG.Common;
using LAMG.Domain.Models;

namespace LAMG.Application.Jobs;

/// <summary>
/// Raised by <see cref="LAMG.Application.Abstractions.IJobOrchestrator"/>
/// when the pipeline reaches
/// <see cref="LAMG.Domain.Enums.JobStage.AwaitingReusePool"/>. The
/// orchestrator pauses until the host calls
/// <c>SubmitReusePoolAsync</c> with the user's batch selection.
/// </summary>
/// <remarks>
/// An empty selection is meaningful: it means the user opted out of
/// reuse-mode and the orchestrator should skip directly to
/// <see cref="LAMG.Domain.Enums.JobStage.Done"/>.
/// </remarks>
public sealed class ReusePoolRequestedEventArgs : EventArgs
{
    public ReusePoolRequestedEventArgs(long jobId, IReadOnlyList<Batch> batches)
    {
        JobId = jobId;
        Batches = Guard.NotNull(batches);
    }

    public long JobId { get; }

    /// <summary>
    /// The imported batches the user can choose from. The orchestrator
    /// already excludes empty batches and batches whose tracks are all
    /// <see cref="LAMG.Domain.Enums.TrackStatus.Corrupted"/>.
    /// </summary>
    public IReadOnlyList<Batch> Batches { get; }
}
