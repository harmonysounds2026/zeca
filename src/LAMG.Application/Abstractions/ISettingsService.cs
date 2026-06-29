using LAMG.Application.Settings;

namespace LAMG.Application.Abstractions;

/// <summary>
/// Typed access to user-scoped settings. Wraps the raw
/// <see cref="LAMG.Application.Abstractions.Persistence.ISettingsRepository"/>
/// behind the typed <see cref="AppSettings"/> contract and caches the
/// current value in memory.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads the current user-scoped settings from the database,
    /// falling back to <see cref="DefaultAppSettings.Value"/> for any
    /// missing keys.
    /// </summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the supplied settings under <c>User</c> scope.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached settings without re-reading the database.
    /// Returns <see cref="DefaultAppSettings.Value"/> when nothing has
    /// been loaded yet.
    /// </summary>
    AppSettings Current { get; }
}
