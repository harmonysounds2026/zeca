namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// Boundary that groups several repository operations into a single
/// SQLite transaction. Implementations:
/// <list type="bullet">
///   <item>open one SQLite connection,</item>
///   <item>begin a transaction,</item>
///   <item>hand the connection + transaction to the action wrapped in
///         a <see cref="DbSession"/>,</item>
///   <item>commit on success or roll back on any exception.</item>
/// </list>
/// </summary>
/// <remarks>
/// The action must thread the supplied <see cref="DbSession"/> into
/// every repository call it makes; otherwise repositories will open
/// their own connections and the transaction will not actually wrap
/// them.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="action"/> inside a single SQLite transaction.
    /// The action receives a <see cref="DbSession"/> bound to the
    /// transactional connection; pass it to every repository call to
    /// keep them inside the transaction.
    /// </summary>
    Task ExecuteAsync(
        Func<DbSession, CancellationToken, Task> action,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="ExecuteAsync(Func{DbSession, CancellationToken, Task}, CancellationToken)"/>
    /// <returns>The value returned by <paramref name="action"/>.</returns>
    Task<T> ExecuteAsync<T>(
        Func<DbSession, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
