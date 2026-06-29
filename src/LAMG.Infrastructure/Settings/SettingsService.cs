using System.Text.Json;
using System.Text.Json.Serialization;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Application.Settings;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Infrastructure.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LAMG.Infrastructure.Settings;

/// <inheritdoc cref="ISettingsService"/>
/// <remarks>
/// Persists the whole <see cref="AppSettings"/> record as a JSON blob
/// under a single User-scoped key
/// (<see cref="SettingsKeys.AppSettingsJson"/>). The in-memory
/// <see cref="Current"/> property is updated on every load/save, so
/// downstream services (e.g. <c>FFmpegLocator</c>, the analyzers) see
/// changes as soon as the user clicks Save.
/// </remarks>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsRepository _repository;
    private readonly InfrastructureOptions _infrastructureOptions;
    private readonly ILogger<SettingsService> _logger;
    private AppSettings _current;

    public SettingsService(
        ISettingsRepository repository,
        IOptions<InfrastructureOptions> infrastructureOptions,
        ILogger<SettingsService> logger)
    {
        _repository = Guard.NotNull(repository);
        ArgumentNullException.ThrowIfNull(infrastructureOptions);
        _infrastructureOptions = Guard.NotNull(infrastructureOptions.Value);
        _logger = Guard.NotNull(logger);

        // Seed with defaults so callers that read Current before
        // LoadAsync has run still get a sane snapshot.
        _current = ApplyEnvironmentDefaults(DefaultAppSettings.Value);
    }

    public AppSettings Current => _current;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        string? json = await _repository
            .GetAsync(SettingsKeys.AppSettingsJson, SettingScope.User, projectId: null, cancellationToken)
            .ConfigureAwait(false);

        AppSettings loaded;

        if (string.IsNullOrWhiteSpace(json))
        {
            loaded = DefaultAppSettings.Value;
            _logger.LogDebug("No persisted settings found; using defaults.");
        }
        else
        {
            try
            {
                loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                         ?? DefaultAppSettings.Value;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Persisted settings JSON could not be parsed; using defaults.");
                loaded = DefaultAppSettings.Value;
            }
        }

        AppSettings effective = ApplyEnvironmentDefaults(loaded);
        _current = effective;
        return effective;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string json = JsonSerializer.Serialize(settings, JsonOptions);

        await _repository
            .SetAsync(
                SettingsKeys.AppSettingsJson,
                json,
                SettingScope.User,
                projectId: null,
                cancellationToken)
            .ConfigureAwait(false);

        _current = ApplyEnvironmentDefaults(settings);
        _logger.LogDebug("Settings saved (cpu={Cpu}, target={Target}min).",
            settings.CpuMode, settings.TargetDurationMinutes);
    }

    /// <summary>
    /// Fills in defaults that depend on per-machine state (e.g. the
    /// default output folder under %LOCALAPPDATA%) when the persisted
    /// value is missing. Keeps <see cref="AppSettings"/> portable.
    /// </summary>
    private AppSettings ApplyEnvironmentDefaults(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OutputFolder)
            && !string.IsNullOrWhiteSpace(_infrastructureOptions.DefaultOutputFolder))
        {
            return settings with { OutputFolder = _infrastructureOptions.DefaultOutputFolder };
        }

        return settings;
    }
}
