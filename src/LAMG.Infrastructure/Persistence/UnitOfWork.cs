using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="IUnitOfWork"/>. Each call:
/// <list type="number">
///   <item>opens a single SQLite connection,</item>
///   <item>begins an <c>IMMEDIATE</c> transaction (the SQLite default),</item>
///   <item>invokes the supplied action with a <see cref="DbSession"/>
///         bound to that connection + transaction,</item>
///   <item>commits on success, or rolls back on any exception.</item>
/// </list>
/// </summary>
/// <remarks>
/// Repositories that receive the same <see cref="DbSession"/> route
/// their Dapper calls onto the supplied connection and pass the
/// transaction through <c>CommandDefinition.Transaction</c>, so every
/// statement they execute is part of the unit's transaction.
/// </remarks>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ILogger<UnitOfWork> _logger;

    public UnitOfWork(
        SqliteConnectionFactory connectionFactory,
        ILogger<UnitOfWork> logger)
    {
        _connectionFactory = Guard.NotNull(connectionFactory);
        _logger = Guard.NotNull(logger);
    }

    public async Task ExecuteAsync(
        Func<DbSession, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using SqliteConnection connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteTransaction tx = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        DbSession session = new(connection, tx);

        try
        {
            await action(session, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UnitOfWork rolling back due to exception.");
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(
        Func<DbSession, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using SqliteConnection connection = await _connectionFactory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await using SqliteTransaction tx = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        DbSession session = new(connection, tx);

        try
        {
            T result = await action(session, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UnitOfWork rolling back due to exception.");
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
