using LAMG.Domain.Enums;
using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// CRUD and query operations over <see cref="Mix"/> and its associated
/// <see cref="MixItem"/> and <see cref="MixBatch"/> rows. Persisted as
/// an aggregate to keep planning atomic.
/// </summary>
/// <remarks>
/// The optional <see cref="DbSession"/> parameter (last on every method)
/// lets callers reuse a shared connection/transaction.
/// <see cref="AddPlannedAsync"/> reuses an outer
/// <see cref="IUnitOfWork"/> transaction when one is provided;
/// otherwise it creates a local one to keep the three-table write
/// atomic.
/// </remarks>
public interface IMixRepository
{
    /// <summary>
    /// Inserts a planned mix together with its items and source-batch
    /// associations in a single transaction. Returns the new mix id.
    /// </summary>
    Task<long> AddPlannedAsync(
        Mix mix,
        IReadOnlyList<MixItem> items,
        IReadOnlyCollection<long> sourceBatchIds,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task UpdateStatusAsync(
        long mixId,
        MixStatus status,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task MarkCompletedAsync(
        long mixId,
        string outputPath,
        int actualDurationSeconds,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<Mix?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<MixItem>> GetItemsAsync(
        long mixId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<long>> GetSourceBatchIdsAsync(
        long mixId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Mix>> GetByProjectAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    /// <summary>
    /// Returns the next planned mix for the given project, ordered by
    /// <see cref="Mix.IndexInProject"/>. Used by the render pipeline to
    /// pick the next unit of work after a resume.
    /// </summary>
    Task<Mix?> GetNextPlannedAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    /// <summary>
    /// Returns the highest <see cref="Mix.IndexInProject"/> in use, or
    /// zero if no mixes exist yet. Used to assign the next index.
    /// </summary>
    Task<int> GetMaxIndexAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);
}
