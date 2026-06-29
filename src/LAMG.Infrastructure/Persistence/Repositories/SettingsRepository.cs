using Dapper;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ISettingsRepository"/>
/// <remarks>
/// The <c>Settings</c> table uses a composite primary key
/// <c>(key, scope, project_id)</c> with <c>project_id NOT NULL DEFAULT 0</c>.
/// User-scoped settings are written with <c>project_id = 0</c>.
/// Project-scoped settings carry the real project id.
/// </remarks>
public sealed class SettingsRepository : ISettingsRepository
{
    /// <summary>Sentinel value stored in the column when scope = User.</summary>
    private const long NoProjectId = 0;

    private const string SelectOneSql = """
        SELECT value FROM Settings
        WHERE key = @key AND scope = @scope AND project_id = @projectId;
        """;

    private const string UpsertSql = """
        INSERT INTO Settings(key, value, scope, project_id)
        VALUES (@key, @value, @scope, @projectId)
        ON CONFLICT(key, scope, project_id) DO UPDATE SET value = excluded.value;
        """;

    private const string SelectAllSql = """
        SELECT key, value FROM Settings
        WHERE scope = @scope AND project_id = @projectId;
        """;

    private const string DeleteSql = """
        DELETE FROM Settings
        WHERE key = @key AND scope = @scope AND project_id = @projectId;
        """;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SettingsRepository> _logger;

    public SettingsRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<SettingsRepository> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public Task<string?> GetAsync(
        string key,
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.NotNullOrWhiteSpace(key);
        ValidateScopeAndProjectId(scope, projectId);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteScalarAsync<string?>(new CommandDefinition(
                SelectOneSql,
                new { key, scope, projectId = projectId ?? NoProjectId },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task SetAsync(
        string key,
        string value,
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.NotNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        ValidateScopeAndProjectId(scope, projectId);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                UpsertSql,
                new { key, value, scope, projectId = projectId ?? NoProjectId },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ValidateScopeAndProjectId(scope, projectId);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            IEnumerable<(string Key, string Value)> rows = await conn
                .QueryAsync<(string Key, string Value)>(new CommandDefinition(
                    SelectAllSql,
                    new { scope, projectId = projectId ?? NoProjectId },
                    transaction: tx,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            Dictionary<string, string> result = new(StringComparer.Ordinal);
            foreach ((string k, string v) in rows)
            {
                result[k] = v;
            }

            return (IReadOnlyDictionary<string, string>)result;
        }, cancellationToken);
    }

    public Task DeleteAsync(
        string key,
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.NotNullOrWhiteSpace(key);
        ValidateScopeAndProjectId(scope, projectId);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.ExecuteAsync(new CommandDefinition(
                DeleteSql,
                new { key, scope, projectId = projectId ?? NoProjectId },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    private static void ValidateScopeAndProjectId(SettingScope scope, long? projectId)
    {
        // User-scope: projectId must be null (or 0). Project-scope: must be a real id.
        switch (scope)
        {
            case SettingScope.User:
                if (projectId is not null and not 0)
                {
                    throw new ArgumentException(
                        "User-scoped settings must use a null project id.",
                        nameof(projectId));
                }
                break;

            case SettingScope.Project:
                if (projectId is null or <= 0)
                {
                    throw new ArgumentException(
                        "Project-scoped settings require a positive project id.",
                        nameof(projectId));
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown scope.");
        }
    }
}
