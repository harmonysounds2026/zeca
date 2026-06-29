using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// CRUD operations over the <see cref="Project"/> aggregate.
/// </summary>
/// <remarks>
/// The optional <see cref="DbSession"/> parameter (last on every method)
/// lets callers reuse a shared connection/transaction. Standalone
/// callers can ignore it.
/// </remarks>
public interface IProjectRepository
{
    Task<long> AddAsync(
        Project project,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<Project?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyList<Project>> GetAllAsync(
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task UpdateAsync(
        Project project,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task DeleteAsync(
        long id,
        CancellationToken cancellationToken = default,
        DbSession? session = null);
}
