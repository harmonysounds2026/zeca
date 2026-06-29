using Dapper;

using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IProjectRepository"/>
public sealed class ProjectRepository : IProjectRepository
{
    private const string InsertSql = """
        INSERT INTO Projects(name, created_at, settings_json, output_folder)
        VALUES (@Name, @CreatedAt, @SettingsJson, @OutputFolder);
        SELECT last_insert_rowid();
        """;

    private const string SelectByIdSql = """
        SELECT id, name, created_at, settings_json, output_folder
        FROM Projects
        WHERE id = @id;
        """;

    private const string SelectAllSql = """
        SELECT id, name, created_at, settings_json, output_folder
        FROM Projects
        ORDER BY created_at DESC, id DESC;
        """;

    private const string UpdateSql = """
        UPDATE Projects
        SET name          = @Name,
            settings_json = @SettingsJson,
            output_folder = @OutputFolder
        WHERE id = @Id;
        """;

    private const string DeleteSql = "DELETE FROM Projects WHERE id = @id;";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<ProjectRepository> _logger;

    public ProjectRepository(
        SqliteConnectionFactory connectionFactory,
        ILogger<ProjectRepository> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public Task<long> AddAsync(
        Project project,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            long id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                InsertSql,
                project,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            project.Id = id;
            _logger.LogDebug("Inserted Project {Id} '{Name}'.", id, project.Name);
            return id;
        }, cancellationToken);
    }

    public Task<Project?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        Guard.Positive(id);

        return DapperSession.RunAsync(session, _connectionFactory, (conn, tx) =>
            conn.QueryFirstOrDefaultAsync<Project>(new CommandDefinition(
                SelectByIdSql,
                new { id },
                transaction: tx,
                cancellationToken: cancellationToken)),
            cancellationToken);
    }

    public Task<IReadOnlyList<Project>> GetAllAsync(
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            IEnumerable<Project> rows = await conn.QueryAsync<Project>(new CommandDefinition(
                SelectAllSql,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return rows as IReadOnlyList<Project> ?? rows.ToList();
        }, cancellationToken);
    }

    public Task UpdateAsync(
        Project project,
        CancellationToken cancellationToken = default,
        DbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        Guard.Positive(project.Id);

        return DapperSession.RunAsync(session, _connectionFactory, async (conn, tx) =>
        {
            int affected = await conn.ExecuteAsync(new CommandDefinition(
                UpdateSql,
                project,
                transaction: tx,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (affected == 0)
            {
                throw new InvalidOperationException(
                    $"Update affected no rows: Project id {project.Id} does not exist.");
            }
        }, cancellationToken);
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
