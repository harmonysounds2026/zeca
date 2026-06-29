namespace LAMG.Domain.Enums;

/// <summary>
/// Lifecycle status of a job. Persisted in the <c>Jobs</c> table so the
/// application can resume after a crash.
/// </summary>
public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
}
