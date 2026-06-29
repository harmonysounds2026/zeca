using LAMG.Domain.Enums;
using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// CRUD and query operations over the <see cref="Track"/> aggregate.
/// </summary>
/// <remarks>
/// The optional <see cref="DbSession"/> parameter (last on every method)
/// lets callers reuse a shared connection/transaction. Standalone
/// callers can ignore it.
/// </remarks>
public interface ITrackRepository
{
    Task<long> AddAsync(
        Track track,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task AddRangeAsync(
        IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task UpdateAsync(
        Track track,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task UpdateStatusAsync(
        long trackId,
        TrackStatus status,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<Track?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Track>> GetByBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Track>> GetByBatchesAsync(
        IReadOnlyCollection<long> batchIds,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Track>> GetReadyByBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Track>> GetReadyByBatchesAsync(
        IReadOnlyCollection<long> batchIds,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Track>> FindByFileHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Track>> FindByAudioHashAsync(
        string audioHash,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Track>> FindByFileNameAsync(
        string fileName,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task IncrementUsageAsync(
        IReadOnlyCollection<long> trackIds,
        DateTimeOffset lastUsedAt,
        CancellationToken cancellationToken = default,
        DbSession? session = null);
}
