using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// CRUD and query operations over the durable <see cref="Job"/> record.
/// All long-running operations heartbeat this row so the application
/// can detect and resume interrupted work.
/// </summary>
/// <remarks>
/// The optional <see cref="DbSession"/> parameter (last on every method)
/// lets callers reuse a shared connection/transaction. Heartbeats in
/// particular benefit from running standalone (no session) so they
/// don't fight for the UoW lock during long writes.
/// </remarks>
public interface IJobRepository
{
    Task<long> AddAsync(
        Job job,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task UpdateAsync(
        Job job,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    /// <summary>
    /// Updates <c>last_heartbeat</c> only, without touching other fields.
    /// Should be safe to call frequently.
    /// </summary>
    Task HeartbeatAsync(
        long jobId,
        DateTimeOffset heartbeatAt,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<Job?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    /// <summary>
    /// Returns jobs in <c>Running</c> or <c>Paused</c> status whose
    /// heartbeat is older than the supplied threshold.
    /// </summary>
    Task<IReadOnlyList<Job>> GetResumableAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken = default,
        DbSession? session = null);
}
