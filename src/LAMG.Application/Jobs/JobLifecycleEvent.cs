using LAMG.Domain.Enums;

namespace LAMG.Application.Jobs;

/// <summary>
/// Notification raised by <see cref="LAMG.Application.Abstractions.IJobOrchestrator"/>
/// whenever the job transitions to a new stage or terminal status.
/// </summary>
public sealed class JobLifecycleEvent : EventArgs
{
    public JobLifecycleEvent(long jobId, JobStage stage, JobStatus status, string? message = null)
    {
        JobId = jobId;
        Stage = stage;
        Status = status;
        Message = message;
        TimestampUtc = DateTimeOffset.UtcNow;
    }

    public long JobId { get; }

    public JobStage Stage { get; }

    public JobStatus Status { get; }

    public string? Message { get; }

    public DateTimeOffset TimestampUtc { get; }
}
