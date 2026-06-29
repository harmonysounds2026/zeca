using LAMG.Application.Jobs;
using LAMG.Common;
using LAMG.Domain.Enums;

namespace LAMG.Application.Abstractions;

/// <summary>
/// Single entry point for starting, controlling and resuming the
/// long-running jobs that produce mixes. All job state transitions
/// flow through this service so the application can recover after a
/// crash.
/// </summary>
public interface IJobOrchestrator
{
    /// <summary>
    /// Begins the full pipeline: import (already done), analyze,
    /// detect duplicates, plan, render. Returns the job id.
    /// </summary>
    Task<long> StartAsync(
        JobRequest request,
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a previously-interrupted job by id, picking up at the
    /// last persisted checkpoint.
    /// </summary>
    Task<Result> ResumeAsync(
        long jobId,
        IProgress<JobProgress>? progress,
        CancellationToken cancellationToken = default);

    /// <summary>Marks the running job as paused. Idempotent.</summary>
    Task PauseAsync(long jobId, CancellationToken cancellationToken = default);

    /// <summary>Cancels the running job. Idempotent.</summary>
    Task CancelAsync(long jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides the reuse-mode batch pool selected by the user. The
    /// orchestrator pauses at <see cref="LAMG.Domain.Enums.JobStage.AwaitingReusePool"/>
    /// until this is called.
    /// </summary>
    Task SubmitReusePoolAsync(
        long jobId,
        IReadOnlyCollection<long> selectedBatchIds,
        CancellationToken cancellationToken = default);

    /// <summary>Raised whenever the orchestrator transitions to a new stage.</summary>
    event EventHandler<JobLifecycleEvent> LifecycleChanged;

    /// <summary>
    /// Raised when duplicate tracks have been detected and the orchestrator
    /// is paused at <see cref="JobStage.DetectingDuplicates"/> awaiting the
    /// user's resolution choice. Subscribers must call
    /// <see cref="SubmitDuplicateResolutionAsync"/> to unblock the job.
    /// </summary>
    event EventHandler<DuplicateResolutionRequestedEventArgs> DuplicateResolutionRequested;

    /// <summary>
    /// Raised when the orchestrator reaches
    /// <see cref="JobStage.AwaitingReusePool"/> and needs the user to
    /// pick which batches feed the reuse-mode pool. Subscribers must
    /// call <see cref="SubmitReusePoolAsync"/> to unblock the job. An
    /// empty selection causes reuse mode to be skipped entirely.
    /// </summary>
    event EventHandler<ReusePoolRequestedEventArgs> ReusePoolRequested;

    /// <summary>
    /// Raised after each mix render finishes (success or failure).
    /// The UI uses this to append rows to its completed-mixes list
    /// in real time without polling the database.
    /// </summary>
    event EventHandler<MixRenderedEventArgs> MixRendered;

    /// <summary>
    /// Provides the duplicate-resolution decision from the user. Calling
    /// this on a job that is not currently waiting for duplicates is a
    /// no-op.
    /// </summary>
    Task SubmitDuplicateResolutionAsync(
        long jobId,
        DuplicateResolution resolution,
        CancellationToken cancellationToken = default);
}
