using System.Data.Common;

using LAMG.Application.Abstractions.Persistence;

using Microsoft.Data.Sqlite;

namespace LAMG.Infrastructure.Persistence;

/// <summary>
/// Threading helper used by every Dapper repository so the same method
/// body can serve two callers without duplicating connection management:
/// <list type="bullet">
///   <item>
///   Standalone - the repository opens (and disposes) its own SQLite
///   connection.
///   </item>
///   <item>
///   Inside a <see cref="UnitOfWork"/> scope - the repository reuses the
///   connection (and transaction) the UoW handed out via
///   <see cref="DbSession"/>.
///   </item>
/// </list>
/// </summary>
internal static class DapperSession
{
    /// <summary>
    /// Runs <paramref name="body"/> against either the session's
    /// connection (with its possibly-null transaction) or a fresh
    /// connection opened from <paramref name="factory"/>. Suitable for
    /// single-statement operations and read queries - atomicity comes
    /// from SQLite itself.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        DbSession? session,
        SqliteConnectionFactory factory,
        Func<DbConnection, DbTransaction?, Task<T>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(body);

        if (session is not null)
        {
            return await body(session.Connection, session.Transaction).ConfigureAwait(false);
        }

        await using SqliteConnection ownConn = await factory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        return await body(ownConn, null).ConfigureAwait(false);
    }

    /// <summary>Non-generic <see cref="RunAsync{T}"/> for fire-and-forget commands.</summary>
    public static async Task RunAsync(
        DbSession? session,
        SqliteConnectionFactory factory,
        Func<DbConnection, DbTransaction?, Task> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(body);

        if (session is not null)
        {
            await body(session.Connection, session.Transaction).ConfigureAwait(false);
            return;
        }

        await using SqliteConnection ownConn = await factory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await body(ownConn, null).ConfigureAwait(false);
    }

    /// <summary>
    /// Same as <see cref="RunAsync{T}"/> but guarantees the body runs
    /// inside a transaction. Use this for multi-statement operations
    /// that must be atomic (for example, inserting a mix together with
    /// its items and source-batch bridges).
    /// </summary>
    /// <remarks>
    /// Behaviour by caller shape:
    /// <list type="bullet">
    ///   <item>
    ///   Session with transaction: use it as-is. The
    ///   <see cref="UnitOfWork"/> owns commit/rollback.
    ///   </item>
    ///   <item>
    ///   Session without transaction: begin and commit a local
    ///   transaction on the supplied connection. This atomicity is local
    ///   to the call; the caller's other operations on that connection
    ///   are not affected.
    ///   </item>
    ///   <item>
    ///   No session: open an owned connection, begin a transaction,
    ///   commit at the end, dispose everything.
    ///   </item>
    /// </list>
    /// </remarks>
    public static async Task<T> RunInTransactionAsync<T>(
        DbSession? session,
        SqliteConnectionFactory factory,
        Func<DbConnection, DbTransaction, Task<T>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(body);

        // Case 1: an outer transaction is already in flight. The UoW
        // commits it; we just borrow the pair.
        if (session is { Transaction: not null } existing)
        {
            return await body(existing.Connection, existing.Transaction!).ConfigureAwait(false);
        }

        // Cases 2 (session without tx) and 3 (no session): we own the
        // transaction lifecycle. Only case 3 also owns the connection.
        SqliteConnection? ownConn = null;
        DbConnection conn;
        if (session is not null)
        {
            conn = session.Connection;
        }
        else
        {
            ownConn = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
            conn = ownConn;
        }

        try
        {
            DbTransaction tx = await conn
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                try
                {
                    T result = await body(conn, tx).ConfigureAwait(false);
                    await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownConn is not null)
            {
                await ownConn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Non-generic <see cref="RunInTransactionAsync{T}"/> for void operations.</summary>
    public static Task RunInTransactionAsync(
        DbSession? session,
        SqliteConnectionFactory factory,
        Func<DbConnection, DbTransaction, Task> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        return RunInTransactionAsync<object?>(session, factory, async (c, t) =>
        {
            await body(c, t).ConfigureAwait(false);
            return null;
        }, cancellationToken);
    }
}
