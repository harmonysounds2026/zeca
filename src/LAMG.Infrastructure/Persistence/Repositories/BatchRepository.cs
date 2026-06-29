using Dapper;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IBatchRepository"/>
public sealed class BatchRepository : IBatchRepository
{
    private const string InsertSql = """
        INSERT INTO Batches(project_id, source_folder, imported_at, track_count)
        VALUES (@ProjectId, @SourceFolder, @ImportedAt, @TrackCount);
        SELECT last_insert_rowid();
        """;

    private const string SelectByIdSql = """
        SELECT id, project_id, source_folder, imported_at, track_count
        FROM Batches
        WHERE id = @id;
        """;

    private const string SelectByProjectSql = """
        SELECT id, project_id, source_folder, imported_at, track_count
        FROM Batches
        WHERE project_id = @projectId
        ORDER BY imported_at ASC, id ASC;
        """;

    private const string UpdateTrackCountSql = """
        UPDATE Batches
        SET track_count = @trackCount
        WHERE id = @batchId;
        """;

    private const string DeleteSql = "DELETE FROM Batches WHERE id = @id;";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<BatchRepository> _logger;

    public BatchRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<BatchRepository> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public Task<long> AddAsync(
        Batch batch,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Guard.Positive(batch.ProjectId);
        Guard.NotNullOrWhiteSpace(batch.SourceFolder);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            long id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                InsertSql,
                batch,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            batch.Id = id;
            _logger.LogDebug(
                "Inserted Batch {Id} for project {ProjectId} from {Folder}.",
                id, batch.ProjectId, batch.SourceFolder);
            return id;
        }, cancellationToken);
    }

    public Task<Batch?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(id);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.QueryFirstOrDefaultAsync<Batch>(new CommandDefinition(
                SelectByIdSql,
                new { id },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<IReadOnlyList<Batch>> GetByProjectAsync(
        long projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(projectId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            IEnumerable<Batch> rows = await conn.QueryAsync<Batch>(new CommandDefinition(
                SelectByProjectSql,
                new { projectId },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows as IReadOnlyList<Batch> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task UpdateTrackCountAsync(
        long batchId,
        int trackCount,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(batchId);
        Guard.NotNegative(trackCount);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                UpdateTrackCountSql,
                new { batchId, trackCount },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(id);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                DeleteSql,
                new { id },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }
}
