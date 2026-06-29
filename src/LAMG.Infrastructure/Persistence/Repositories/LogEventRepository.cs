using Dapper;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ILogEventRepository"/>
public sealed class LogEventRepository : ILogEventRepository
{
    private const string InsertSql = """
        INSERT INTO LogEvents(created_at, level, source, message, exception, context_json)
        VALUES (@CreatedAt, @Level, @Source, @Message, @Exception, @ContextJson);
        """;

    private const string SelectRecentSql = """
        SELECT id, created_at, level, source, message, exception, context_json
        FROM LogEvents
        ORDER BY created_at DESC, id DESC
        LIMIT @max;
        """;

    private const string PurgeBeforeSql = """
        DELETE FROM LogEvents WHERE created_at < @cutoff;
        """;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<LogEventRepository> _logger;

    public LogEventRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<LogEventRepository> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public Task AddAsync(
        LogEvent logEvent,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                InsertSql,
                logEvent,
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<IReadOnlyList<LogEvent>> GetRecentAsync(
        int max,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(max);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            IEnumerable<LogEvent> rows = await conn.QueryAsync<LogEvent>(new CommandDefinition(
                SelectRecentSql,
                new { max },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows as IReadOnlyList<LogEvent> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task<int> PurgeBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            int affected = await conn.ExecuteAsync(new CommandDefinition(
                PurgeBeforeSql,
                new { cutoff },
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (affected > 0)
            {
                _logger.LogDebug("Purged {Count} log events older than {Cutoff:O}.", affected, cutoff);
            }

            return affected;
        }, cancellationToken);
    }
}
