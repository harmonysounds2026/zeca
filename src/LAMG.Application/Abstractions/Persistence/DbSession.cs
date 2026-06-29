using System.Data.Common;

using LAMG.Common;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// A shared database session: an open <see cref="DbConnection"/> with
/// an optional in-flight <see cref="DbTransaction"/>. Repositories
/// accept this as an optional parameter so several operations can run
/// against the same connection - and, when present, the same
/// transaction - without each repository opening its own connection.
/// </summary>
/// <remarks>
/// <para>
/// Sessions are produced by <see cref="IUnitOfWork.ExecuteAsync(System.Func{DbSession, System.Threading.CancellationToken, System.Threading.Tasks.Task}, System.Threading.CancellationToken)"/>;
/// callers should not construct them directly. The connection and
/// transaction are owned by the <see cref="IUnitOfWork"/> that created
/// the session and must remain valid for the lifetime of the call.
/// </para>
/// <para>
/// Why the session is the last parameter on every repository method:
/// adding it before <c>CancellationToken</c> would break every existing
/// positional call site, because <c>CancellationToken</c> does not
/// convert to <see cref="DbSession"/>?. Placing it last preserves source
/// compatibility - callers that opt in to a shared session pass it
/// explicitly, callers that don't are unchanged.
/// </para>
/// </remarks>
public sealed class DbSession
{
    public DbSession(DbConnection connection, DbTransaction? transaction = null)
    {
        Connection = Guard.NotNull(connection);
        Transaction = transaction;
    }

    /// <summary>The open SQLite connection to use for Dapper commands.</summary>
    public DbConnection Connection { get; }

    /// <summary>
    /// The active transaction, if any. <c>null</c> means "no transaction
    /// in flight - statements run auto-committed against
    /// <see cref="Connection"/>".
    /// </summary>
    public DbTransaction? Transaction { get; }
}
