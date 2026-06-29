using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Application.Abstractions.System;
using LAMG.Infrastructure.Audio;
using LAMG.Infrastructure.Configuration;
using LAMG.Infrastructure.CrashRecovery;
using LAMG.Infrastructure.FFmpeg;
using LAMG.Infrastructure.FileSystem;
using LAMG.Infrastructure.Logging;
using LAMG.Infrastructure.Mixing;
using LAMG.Infrastructure.Persistence;
using LAMG.Infrastructure.Persistence.Repositories;
using LAMG.Infrastructure.Power;
using LAMG.Infrastructure.Process;
using LAMG.Infrastructure.Settings;

// Standard pattern: extension methods live in the DI namespace.
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registrations for the LAMG infrastructure layer. Call after
/// <c>AddLamgApplication</c>.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddLamgInfrastructure(
        this IServiceCollection services,
        Action<InfrastructureOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.AddOptions<InfrastructureOptions>().Configure(configureOptions);

        // ---- Persistence ----
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<IUnitOfWork, UnitOfWork>();

        services.AddSingleton<IProjectRepository, ProjectRepository>();
        services.AddSingleton<IBatchRepository, BatchRepository>();
        services.AddSingleton<ITrackRepository, TrackRepository>();
        services.AddSingleton<IMixRepository, MixRepository>();
        services.AddSingleton<IJobRepository, JobRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<ILogEventRepository, LogEventRepository>();

        // ---- FFmpeg layer ----
        services.AddSingleton<IFFmpegLocator, FFmpegLocator>();
        services.AddSingleton<FFmpegRunner>();
        services.AddSingleton<FFprobeRunner>();
        services.AddSingleton<FilterGraphBuilder>();

        // ---- Audio services ----
        services.AddSingleton<IBatchImportService, BatchImportService>();
        services.AddSingleton<IFFprobeService, FFprobeService>();
        services.AddSingleton<IFileHasher, FileHasher>();
        services.AddSingleton<IAudioHasher, AudioHasher>();
        services.AddSingleton<ISilenceDetector, SilenceDetector>();
        services.AddSingleton<ILoudnessAnalyzer, LoudnessAnalyzer>();
        services.AddSingleton<IAudioAnalysisService, AudioAnalysisService>();
        services.AddSingleton<IDuplicateDetector, DuplicateDetector>();

        // ---- Mixing ----
        services.AddSingleton<IMixRenderer, MixRenderer>();

        // ---- Cross-cutting ----
        services.AddSingleton<ICpuModeApplier, CpuModeApplier>();
        services.AddSingleton<ISystemAwakeKeeper, SystemAwakeKeeper>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ICrashRecoveryService, CrashRecoveryService>();

        // Logging sink is registered as both ILogReader (consumed by
        // the UI) and as a concrete InMemoryLogSink (attached to the
        // Serilog pipeline at host startup).
        services.AddSingleton<InMemoryLogSink>();
        services.AddSingleton<ILogReader>(sp => sp.GetRequiredService<InMemoryLogSink>());

        return services;
    }

    /// <summary>
    /// Convenience overload that uses <see cref="LamgPaths.BuildDefaultOptions"/>.
    /// </summary>
    public static IServiceCollection AddLamgInfrastructure(this IServiceCollection services)
    {
        InfrastructureOptions defaults = LamgPaths.BuildDefaultOptions();
        return services.AddLamgInfrastructure(opts =>
        {
            opts.DatabasePath = defaults.DatabasePath;
            opts.LogsFolder = defaults.LogsFolder;
            opts.DefaultOutputFolder = defaults.DefaultOutputFolder;
            opts.FFmpegBundledFolder = defaults.FFmpegBundledFolder;
        });
    }
}
