using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// CRUD operations over the <see cref="Batch"/> aggregate.
/// </summary>
/// <remarks>
/// The optional <see cref="DbSession"/> parameter (last on every method)
/// lets callers reuse a shared connection/transaction. Standalone
/// callers can ignore it.
/// </remarks>
public interface IBatchRepository
{
    Task<long> AddAsync(
        Batch batch,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<Batch?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Batch>> GetByProjectAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task UpdateTrackCountAsync(
        long batchId,
        int trackCount,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null);
}
