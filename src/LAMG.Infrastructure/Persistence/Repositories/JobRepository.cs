using Dapper;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IJobRepository"/>
public sealed class JobRepository : IJobRepository
{
    private const string InsertSql = """
        INSERT INTO Jobs(project_id, job_type, status, current_stage, last_heartbeat,
                         payload_json, created_at, finished_at)
        VALUES (@ProjectId, @JobType, @Status, @CurrentStage, @LastHeartbeat,
                @PayloadJson, @CreatedAt, @FinishedAt);
        SELECT last_insert_rowid();
        """;

    private const string UpdateSql = """
        UPDATE Jobs
        SET project_id     = @ProjectId,
            job_type       = @JobType,
            status         = @Status,
            current_stage  = @CurrentStage,
            last_heartbeat = @LastHeartbeat,
            payload_json   = @PayloadJson,
            finished_at    = @FinishedAt
        WHERE id = @Id;
        """;

    private const string HeartbeatSql = """
        UPDATE Jobs
        SET last_heartbeat = @heartbeatAt
        WHERE id = @jobId;
        """;

    private const string SelectByIdSql = """
        SELECT id, project_id, job_type, status, current_stage, last_heartbeat,
               payload_json, created_at, finished_at
        FROM Jobs
        WHERE id = @id;
        """;

    private const string SelectResumableSql = """
        SELECT id, project_id, job_type, status, current_stage, last_heartbeat,
               payload_json, created_at, finished_at
        FROM Jobs
        WHERE status IN @statuses AND last_heartbeat < @stale
        ORDER BY last_heartbeat DESC, id DESC;
        """;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<JobRepository> _logger;

    public JobRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<JobRepository> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public Task<long> AddAsync(
        Job job,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(job);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            long id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                InsertSql,
                job,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            job.Id = id;
            _logger.LogDebug(
                "Inserted Job {Id} (type {Type}, status {Status}).",
                id, job.JobType, job.Status);
            return id;
        }, cancellationToken);
    }

    public Task UpdateAsync(
        Job job,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        Guard.Positive(job.Id);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            int affected = await conn.ExecuteAsync(new CommandDefinition(
                UpdateSql,
                job,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException(
                    $"Update affected no rows: Job id {job.Id} does not exist.");
            }
        }, cancellationToken);
    }

    public Task HeartbeatAsync(
        long jobId,
        DateTimeOffset heartbeatAt,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(jobId);

        // Heartbeat is fire-and-forget semantics: a stale id is harmless.
        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                HeartbeatSql,
                new { jobId, heartbeatAt },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<Job?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(id);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.QueryFirstOrDefaultAsync<Job>(new CommandDefinition(
                SelectByIdSql,
                new { id },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<IReadOnlyList<Job>> GetResumableAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            IEnumerable<Job> rows = await conn.QueryAsync<Job>(new CommandDefinition(
                SelectResumableSql,
                new
                {
                    statuses = new[] { JobStatus.Running, JobStatus.Paused },
                    stale = staleBefore,
                },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<Job> ?? rows.ToList();
        }, cancellationToken);
    }
}
