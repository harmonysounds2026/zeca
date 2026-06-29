using Dapper;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ITrackRepository"/>
public sealed class TrackRepository : ITrackRepository
{
    private const string Columns = """
        id, batch_id, full_path, file_name, format,
        file_size_bytes, duration_ms, sample_rate, channels, bitrate_kbps,
        file_hash, audio_hash, silence_lead_ms, silence_tail_ms,
        integrated_lufs, true_peak_db, status, times_used, last_used_at
        """;

    private const string InsertSql = """
        INSERT INTO Tracks(
            batch_id, full_path, file_name, format,
            file_size_bytes, duration_ms, sample_rate, channels, bitrate_kbps,
            file_hash, audio_hash, silence_lead_ms, silence_tail_ms,
            integrated_lufs, true_peak_db, status, times_used, last_used_at)
        VALUES (
            @BatchId, @FullPath, @FileName, @Format,
            @FileSizeBytes, @DurationMs, @SampleRate, @Channels, @BitrateKbps,
            @FileHash, @AudioHash, @SilenceLeadMs, @SilenceTailMs,
            @IntegratedLufs, @TruePeakDb, @Status, @TimesUsed, @LastUsedAt);
        SELECT last_insert_rowid();
        """;

    private const string InsertNoReturnSql = """
        INSERT INTO Tracks(
            batch_id, full_path, file_name, format,
            file_size_bytes, duration_ms, sample_rate, channels, bitrate_kbps,
            file_hash, audio_hash, silence_lead_ms, silence_tail_ms,
            integrated_lufs, true_peak_db, status, times_used, last_used_at)
        VALUES (
            @BatchId, @FullPath, @FileName, @Format,
            @FileSizeBytes, @DurationMs, @SampleRate, @Channels, @BitrateKbps,
            @FileHash, @AudioHash, @SilenceLeadMs, @SilenceTailMs,
            @IntegratedLufs, @TruePeakDb, @Status, @TimesUsed, @LastUsedAt);
        """;

    private const string UpdateSql = """
        UPDATE Tracks
        SET batch_id        = @BatchId,
            full_path       = @FullPath,
            file_name       = @FileName,
            format          = @Format,
            file_size_bytes = @FileSizeBytes,
            duration_ms     = @DurationMs,
            sample_rate     = @SampleRate,
            channels        = @Channels,
            bitrate_kbps    = @BitrateKbps,
            file_hash       = @FileHash,
            audio_hash      = @AudioHash,
            silence_lead_ms = @SilenceLeadMs,
            silence_tail_ms = @SilenceTailMs,
            integrated_lufs = @IntegratedLufs,
            true_peak_db    = @TruePeakDb,
            status          = @Status,
            times_used      = @TimesUsed,
            last_used_at    = @LastUsedAt
        WHERE id = @Id;
        """;

    private const string UpdateStatusSql = """
        UPDATE Tracks SET status = @status WHERE id = @id;
        """;

    private const string IncrementUsageSql = """
        UPDATE Tracks
        SET times_used   = times_used + 1,
            last_used_at = @lastUsedAt
        WHERE id IN @ids;
        """;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<TrackRepository> _logger;

    public TrackRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<TrackRepository> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public Task<long> AddAsync(
        Track track,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        Guard.Positive(track.BatchId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            long id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                InsertSql,
                track,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            track.Id = id;
            return id;
        }, cancellationToken);
    }

    public Task AddRangeAsync(
        IEnumerable<Track> tracks,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        // Materialize once: we may need to iterate twice if the source
        // is a deferred enumerable (e.g. LINQ), and Dapper's bulk
        // parameter binding inside a transaction must see the same items.
        IReadOnlyList<Track> list = tracks as IReadOnlyList<Track> ?? tracks.ToList();
        if (list.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Bulk insert is N statements - must run inside a transaction
        // so a mid-failure rolls back any partial rows.
        return DapperSession.RunInTransactionAsync(session, _connectionFactory, async (conn, tx) =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                InsertNoReturnSql,
                list,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            _logger.LogDebug("Bulk inserted {Count} tracks.", list.Count);
        }, cancellationToken);
    }

    public Task UpdateAsync(
        Track track,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(track);
        Guard.Positive(track.Id);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            int affected = await conn.ExecuteAsync(new CommandDefinition(
                UpdateSql,
                track,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException(
                    $"Update affected no rows: Track id {track.Id} does not exist.");
            }
        }, cancellationToken);
    }

    public Task UpdateStatusAsync(
        long trackId,
        TrackStatus status,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(trackId);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                UpdateStatusSql,
                new { id = trackId, status },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<Track?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(id);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
        {
            string sql = $"SELECT {Columns} FROM Tracks WHERE id = @id;";
            return conn.QueryFirstOrDefaultAsync<Track>(new CommandDefinition(
                sql,
                new { id },
                transaction: tx,
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Track>> GetByBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(batchId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {Columns}
                FROM Tracks
                WHERE batch_id = @batchId
                ORDER BY file_name ASC, id ASC;
                """;

            IEnumerable<Track> rows = await conn.QueryAsync<Track>(new CommandDefinition(
                sql,
                new { batchId },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Track> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Track>> GetByBatchesAsync(
        IReadOnlyCollection<long> batchIds,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(batchIds);
        if (batchIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>());
        }

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {Columns}
                FROM Tracks
                WHERE batch_id IN @batchIds
                ORDER BY batch_id ASC, file_name ASC, id ASC;
                """;

            IEnumerable<Track> rows = await conn.QueryAsync<Track>(new CommandDefinition(
                sql,
                new { batchIds },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Track> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Track>> GetReadyByBatchAsync(
        long batchId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(batchId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {Columns}
                FROM Tracks
                WHERE batch_id = @batchId AND status = @status
                ORDER BY file_name ASC, id ASC;
                """;

            IEnumerable<Track> rows = await conn.QueryAsync<Track>(new CommandDefinition(
                sql,
                new { batchId, status = TrackStatus.Ready },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Track> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Track>> GetReadyByBatchesAsync(
        IReadOnlyCollection<long> batchIds,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(batchIds);
        if (batchIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Track>>(Array.Empty<Track>());
        }

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {Columns}
                FROM Tracks
                WHERE batch_id IN @batchIds AND status = @status
                ORDER BY batch_id ASC, file_name ASC, id ASC;
                """;

            IEnumerable<Track> rows = await conn.QueryAsync<Track>(new CommandDefinition(
                sql,
                new { batchIds, status = TrackStatus.Ready },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Track> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Track>> FindByFileHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.NotNullOrWhiteSpace(fileHash);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {Columns} FROM Tracks WHERE file_hash = @fileHash;
                """;

            IEnumerable<Track> rows = await conn.QueryAsync<Track>(new CommandDefinition(
                sql,
                new { fileHash },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Track> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Track>> FindByAudioHashAsync(
        string audioHash,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.NotNullOrWhiteSpace(audioHash);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {Columns} FROM Tracks WHERE audio_hash = @audioHash;
                """;

            IEnumerable<Track> rows = await conn.QueryAsync<Track>(new CommandDefinition(
                sql,
                new { audioHash },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Track> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Track>> FindByFileNameAsync(
        string fileName,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.NotNullOrWhiteSpace(fileName);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            // COLLATE NOCASE gives ASCII case-insensitive equality. Adequate
            // for v1 file-name duplicate detection.
            string sql = $"""
                SELECT {Columns}
                FROM Tracks
                WHERE file_name = @fileName COLLATE NOCASE;
                """;

            IEnumerable<Track> rows = await conn.QueryAsync<Track>(new CommandDefinition(
                sql,
                new { fileName },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Track> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task IncrementUsageAsync(
        IReadOnlyCollection<long> trackIds,
        DateTimeOffset lastUsedAt,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(trackIds);
        if (trackIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        // Single UPDATE with `WHERE id IN (...)`, atomic on its own.
        // No need for a wrapping transaction.
        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                IncrementUsageSql,
                new { ids = trackIds, lastUsedAt },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }
}
