using LAMG.Domain.Enums;

namespace LAMG.Application.Abstractions.Persistence;

/// <summary>
/// Untyped key/value access over the <c>Settings</c> table. Typed access
/// goes through <see cref="ISettingsService"/>.
/// </summary>
/// <remarks>
/// The optional <see cref="DbSession"/> parameter (last on every method)
/// lets callers reuse a shared connection/transaction.
/// </remarks>
public interface ISettingsRepository
{
    Task<string?> GetAsync(
        string key,
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task SetAsync(
        string key,
        string value,
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);

    Task DeleteAsync(
        string key,
        SettingScope scope,
        long? projectId,
        CancellationToken cancellationToken = default,
        DbSession? session = null);
}
