using LAMG.Common;
using LAMG.Infrastructure.Configuration;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LAMG.Infrastructure.Persistence;

/// <summary>
/// Opens SQLite connections configured for a long-running desktop app:
/// WAL journal mode, NORMAL synchronous, foreign keys ON, and a
/// generous busy timeout. Designed for the connection-per-operation
/// pattern; <see cref="IUnitOfWork"/> groups multiple operations into
/// a single connection + transaction.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly InfrastructureOptions _options;
    private readonly ILogger<SqliteConnectionFactory> _logger;

    public SqliteConnectionFactory(
        IOptions<InfrastructureOptions> options,
        ILogger<SqliteConnectionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = Guard.NotNull(options.Value);
        _logger = Guard.NotNull(logger);
    }

    /// <summary>
    /// Opens a fresh <see cref="SqliteConnection"/>, applies the
    /// standard PRAGMAs, and returns it ready to use. The caller owns
    /// the connection and must dispose it.
    /// </summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.DatabasePath))
        {
            throw new InvalidOperationException(
                "InfrastructureOptions.DatabasePath is empty. " +
                "Configure it at host startup via LamgPaths.BuildDefaultOptions().");
        }

        string? folder = Path.GetDirectoryName(_options.DatabasePath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = 30,
        };

        SqliteConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task ApplyPragmasAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        // Order matters: journal_mode before synchronous.
        await ExecutePragmaAsync(connection, "journal_mode=WAL", cancellationToken)
            .ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "synchronous=NORMAL", cancellationToken)
            .ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "foreign_keys=ON", cancellationToken)
            .ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "temp_store=MEMORY", cancellationToken)
            .ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "busy_timeout=30000", cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("SQLite connection opened at {Path} with WAL pragmas applied.",
            _options.DatabasePath);
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string pragma,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {pragma};";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
