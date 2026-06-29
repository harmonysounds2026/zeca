using LAMG.Domain.Enums;

namespace LAMG.Domain.Models;

/// <summary>
/// Durable record of a long-running unit of work. Updated by
/// the job orchestrator on every state transition and heartbeat so
/// the application can recover after a crash, power loss, or
/// user-initiated pause.
/// </summary>
public sealed class Job
{
    public long Id { get; set; }

    public long? ProjectId { get; set; }

    public JobType JobType { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public JobStage CurrentStage { get; set; } = JobStage.NotStarted;

    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Opaque, serialized resume context: the planning cursor, the
    /// reuse-pool batch ids, retry counts, etc.
    /// </summary>
    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
