using Dapper;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IMixRepository"/>
/// <remarks>
/// <see cref="AddPlannedAsync"/> writes to three tables (Mixes,
/// MixItems, MixBatches) inside a single transaction so planning is
/// either fully durable or not visible at all. When invoked inside a
/// <see cref="UnitOfWork"/> the outer transaction is reused; when
/// invoked standalone a local transaction is created and committed.
/// All other reads and updates are single-statement and atomic on
/// their own.
/// </remarks>
public sealed class MixRepository : IMixRepository
{
    private const string MixColumns = """
        id, project_id, index_in_project, target_min, actual_sec,
        mode, output_format, output_path, created_at, status
        """;

    private const string MixItemColumns = """
        id, mix_id, track_id, order_index, trimmed_ms, xfade_in_ms, xfade_out_ms
        """;

    private const string InsertMixSql = """
        INSERT INTO Mixes(
            project_id, index_in_project, target_min, actual_sec,
            mode, output_format, output_path, created_at, status)
        VALUES (
            @ProjectId, @IndexInProject, @TargetMin, @ActualSec,
            @Mode, @OutputFormat, @OutputPath, @CreatedAt, @Status);
        SELECT last_insert_rowid();
        """;

    private const string InsertMixItemSql = """
        INSERT INTO MixItems(mix_id, track_id, order_index, trimmed_ms, xfade_in_ms, xfade_out_ms)
        VALUES (@MixId, @TrackId, @OrderIndex, @TrimmedMs, @XfadeInMs, @XfadeOutMs);
        """;

    private const string InsertMixBatchSql = """
        INSERT INTO MixBatches(mix_id, batch_id) VALUES (@MixId, @BatchId);
        """;

    private const string UpdateStatusSql = """
        UPDATE Mixes SET status = @status WHERE id = @id;
        """;

    private const string MarkCompletedSql = """
        UPDATE Mixes
        SET status      = @status,
            output_path = @outputPath,
            actual_sec  = @actualSec
        WHERE id = @id;
        """;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<MixRepository> _logger;

    public MixRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<MixRepository> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public Task<long> AddPlannedAsync(
        Mix mix,
        IReadOnlyList<MixItem> items,
        IReadOnlyCollection<long> sourceBatchIds,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(mix);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(sourceBatchIds);
        Guard.Positive(mix.ProjectId);
        Guard.Positive(mix.IndexInProject);

        // Multi-table insert (Mixes + MixItems + MixBatches) must be
        // atomic. RunInTransactionAsync reuses an outer UnitOfWork
        // transaction when present, otherwise creates a local one.
        return DapperSession.RunInTransactionAsync(session, _connectionFactory, async (conn, tx) =>
        {
            // 1. Mix row.
            long mixId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                InsertMixSql,
                mix,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            mix.Id = mixId;

            // 2. MixItems. Stamp the new mix id on each item, then bulk-insert.
            if (items.Count > 0)
            {
                foreach (MixItem item in items)
                {
                    item.MixId = mixId;
                }

                await conn.ExecuteAsync(new CommandDefinition(
                    InsertMixItemSql,
                    items,
                    transaction: tx,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            // 3. MixBatches (batches that contributed tracks to this mix).
            if (sourceBatchIds.Count > 0)
            {
                object[] bridges = sourceBatchIds
                    .Select(bid => (object)new { MixId = mixId, BatchId = bid })
                    .ToArray();

                await conn.ExecuteAsync(new CommandDefinition(
                    InsertMixBatchSql,
                    bridges,
                    transaction: tx,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "Planned mix {MixId} (project {ProjectId}, index {Index}, mode {Mode}) " +
                "with {Items} items and {Batches} source batches.",
                mixId, mix.ProjectId, mix.IndexInProject, mix.Mode,
                items.Count, sourceBatchIds.Count);

            return mixId;
        }, cancellationToken);
    }

    public Task UpdateStatusAsync(
        long mixId,
        MixStatus status,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(mixId);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                UpdateStatusSql,
                new { id = mixId, status },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task MarkCompletedAsync(
        long mixId,
        string outputPath,
        int actualDurationSeconds,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(mixId);
        Guard.NotNullOrWhiteSpace(outputPath);
        Guard.NotNegative(actualDurationSeconds);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            int affected = await conn.ExecuteAsync(new CommandDefinition(
                MarkCompletedSql,
                new
                {
                    id = mixId,
                    status = MixStatus.Completed,
                    outputPath,
                    actualSec = actualDurationSeconds,
                },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException(
                    $"MarkCompletedAsync affected no rows: Mix id {mixId} does not exist.");
            }
        }, cancellationToken);
    }

    public Task<Mix?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(id);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
        {
            string sql = $"SELECT {MixColumns} FROM Mixes WHERE id = @id;";
            return conn.QueryFirstOrDefaultAsync<Mix>(new CommandDefinition(
                sql,
                new { id },
                transaction: tx,
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public Task<IReadOnlyList<MixItem>> GetItemsAsync(
        long mixId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(mixId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {MixItemColumns}
                FROM MixItems
                WHERE mix_id = @mixId
                ORDER BY order_index ASC;
                """;

            IEnumerable<MixItem> rows = await conn.QueryAsync<MixItem>(new CommandDefinition(
                sql,
                new { mixId },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<MixItem> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<long>> GetSourceBatchIdsAsync(
        long mixId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(mixId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            const string sql = """
                SELECT batch_id FROM MixBatches
                WHERE mix_id = @mixId
                ORDER BY batch_id ASC;
                """;

            IEnumerable<long> rows = await conn.QueryAsync<long>(new CommandDefinition(
                sql,
                new { mixId },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<long> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<Mix>> GetByProjectAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(projectId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            string sql = $"""
                SELECT {MixColumns}
                FROM Mixes
                WHERE project_id = @projectId
                ORDER BY index_in_project ASC, id ASC;
                """;

            IEnumerable<Mix> rows = await conn.QueryAsync<Mix>(new CommandDefinition(
                sql,
                new { projectId },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Mix> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<Mix?> GetNextPlannedAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(projectId);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
        {
            string sql = $"""
                SELECT {MixColumns}
                FROM Mixes
                WHERE project_id = @projectId AND status = @status
                ORDER BY index_in_project ASC, id ASC
                LIMIT 1;
                """;

            return conn.QueryFirstOrDefaultAsync<Mix>(new CommandDefinition(
                sql,
                new { projectId, status = MixStatus.Planned },
                transaction: tx,
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }

    public Task<int> GetMaxIndexAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(projectId);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
        {
            const string sql = """
                SELECT COALESCE(MAX(index_in_project), 0)
                FROM Mixes
                WHERE project_id = @projectId;
                """;

            return conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sql,
                new { projectId },
                transaction: tx,
                cancellationToken: cancellationToken));
        }, cancellationToken);
    }
}
