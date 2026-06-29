using System.Reflection;

using Dapper;

using LAMG.Common;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence;

/// <summary>
/// Applies every embedded SQL migration whose name has not yet been
/// recorded in the <c>_SchemaVersion</c> table. Migrations are sorted
/// lexically and applied in a single transaction per file.
/// </summary>
/// <remarks>
/// Migration files must be embedded resources under
/// <c>Persistence\Migrations\NNNN_*.sql</c>. The numeric prefix is
/// what determines apply order and what is stored as
/// <c>_SchemaVersion.version</c>.
/// </remarks>
public sealed class MigrationRunner
{
    private const string MigrationResourcePrefix
        = "LAMG.Infrastructure.Persistence.Migrations.";

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(
        SqliteConnectionFactory connectionFactory,
        ILogger<MigrationRunner> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await EnsureSchemaVersionTableAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<MigrationScript> available = ReadEmbeddedMigrations();
        HashSet<int> applied = await ReadAppliedVersionsAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        foreach (MigrationScript migration in available)
        {
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            _logger.LogInformation(
                "Applying migration {Version:D4} ({ResourceName}).",
                migration.Version,
                migration.ResourceName);

            await ApplyOneAsync(connection, migration, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EnsureSchemaVersionTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS _SchemaVersion (
                version    INTEGER PRIMARY KEY,
                applied_at INTEGER NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HashSet<int>> ReadAppliedVersionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        IEnumerable<int> versions = await connection
            .QueryAsync<int>(new CommandDefinition(
                "SELECT version FROM _SchemaVersion;",
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return new HashSet<int>(versions);
    }

    private static async Task ApplyOneAsync(
        SqliteConnection connection,
        MigrationScript migration,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction tx = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await connection
                .ExecuteAsync(new CommandDefinition(
                    migration.Sql,
                    transaction: tx,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            await connection
                .ExecuteAsync(new CommandDefinition(
                    "INSERT INTO _SchemaVersion(version, applied_at) VALUES (@v, @t);",
                    new { v = migration.Version, t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                    transaction: tx,
                    cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static IReadOnlyList<MigrationScript> ReadEmbeddedMigrations()
    {
        Assembly assembly = typeof(MigrationRunner).Assembly;
        List<MigrationScript> scripts = [];

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(MigrationResourcePrefix, StringComparison.Ordinal)
                || !resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string shortName = resourceName[MigrationResourcePrefix.Length..];
            if (!TryParseVersion(shortName, out int version))
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded migration {resourceName} could not be opened.");

            using StreamReader reader = new(stream);
            string sql = reader.ReadToEnd();
            scripts.Add(new MigrationScript(version, resourceName, sql));
        }

        scripts.Sort(static (a, b) => a.Version.CompareTo(b.Version));
        return scripts;
    }

    /// <summary>
    /// Recognises resource names of the form <c>NNNN_*.sql</c> or
    /// <c>_NNNN_*.sql</c> (MSBuild prefixes a leading digit with '_').
    /// </summary>
    private static bool TryParseVersion(string shortName, out int version)
    {
        ReadOnlySpan<char> s = shortName.AsSpan();
        if (s.Length > 0 && s[0] == '_')
        {
            s = s[1..];
        }

        int end = 0;
        while (end < s.Length && char.IsDigit(s[end]))
        {
            end++;
        }

        if (end == 0)
        {
            version = 0;
            return false;
        }

        return int.TryParse(s[..end], out version);
    }

    private sealed record MigrationScript(int Version, string ResourceName, string Sql);
}
