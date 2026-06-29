using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions;

/// <summary>
/// Inspects the durable <c>Jobs</c> table at application startup and
/// surfaces jobs that look interrupted (status Running/Paused with a
/// stale heartbeat).
/// </summary>
public interface ICrashRecoveryService
{
    Task<IReadOnlyList<Job>> FindResumableJobsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes orphan temporary files (<c>*.tmp</c>) belonging to mixes
    /// that were rendering when the application stopped.
    /// </summary>
    Task CleanupOrphansAsync(
        Job job,
        CancellationToken cancellationToken = default);
}
