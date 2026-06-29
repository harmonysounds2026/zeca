using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// Append-only access to the <c>LogEvents</c> table. Only Warning level
/// and above are persisted; lower severities go to the rolling file
/// sink and the in-memory ring buffer.
/// </summary>
/// <remarks>
/// The optional <see cref="DbSession"/> parameter (last on every method)
/// lets callers reuse a shared connection/transaction.
/// </remarks>
public interface ILogEventRepository
{
    Task AddAsync(
        LogEvent logEvent,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<LogEvent>> GetRecentAsync(
        int max,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    /// <summary>
    /// Deletes rows older than the supplied cutoff. Used by the
    /// retention housekeeping pass.
    /// </summary>
    Task<int> PurgeBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default,
        DbSession? session = null);
}
