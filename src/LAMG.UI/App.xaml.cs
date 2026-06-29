using System.IO;
using System.Windows;
using System.Windows.Threading;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Application.Jobs;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;
using LAMG.Infrastructure.Configuration;
using LAMG.Infrastructure.Logging;
using LAMG.Infrastructure.Persistence;
using LAMG.UI.Services;
using LAMG.UI.ViewModels;
using LAMG.UI.Views;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Events;

namespace LAMG.UI;

/// <summary>
/// Application entry point. Builds the generic host, configures
/// Serilog, runs database migrations, then shows the shell window.
/// </summary>
/// <remarks>
/// Base class is fully qualified because the unqualified name
/// <c>Application</c> would resolve to the sibling namespace
/// <c>LAMG.Application</c> (which is in scope inside <c>LAMG.UI</c>)
/// instead of the WPF <c>System.Windows.Application</c> class.
/// </remarks>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private ILogger<App>? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Resolve all user-visible paths up front so we can configure
        // both Serilog and the Infrastructure options consistently.
        InfrastructureOptions paths = LamgPaths.BuildDefaultOptions();
        Directory.CreateDirectory(paths.LogsFolder);

        try
        {
            _host = BuildHost(paths);
            await _host.StartAsync().ConfigureAwait(true);

            // Configure Dapper before any repository runs.
            DapperConfiguration.EnsureConfigured();

            // Apply any pending schema migrations.
            MigrationRunner migrations = _host.Services.GetRequiredService<MigrationRunner>();
            await migrations.ApplyAsync().ConfigureAwait(true);

            // Load user settings so dependent services (FFmpegLocator,
            // analyzers) see the persisted values from the first call.
            ISettingsService settings = _host.Services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync().ConfigureAwait(true);

            _logger = _host.Services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("Longform Audio Mix Generator starting (v1 scaffold).");

            // Wire global unhandled-exception handlers so background
            // failures show up in the log instead of silently killing
            // the process.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            ShellWindow shell = _host.Services.GetRequiredService<ShellWindow>();
            MainWindow = shell;
            shell.Show();

            // Defer the crash-recovery check until the shell is
            // visible so the modal has a real owner window. The
            // handler unhooks itself after the first invocation.
            shell.Loaded += OnShellLoadedForCrashRecovery;
        }
        catch (Exception ex)
        {
            // Cannot rely on the logger here; the host may have failed
            // to build. Surface a message box and exit gracefully.
            MessageBox.Show(
                ex.ToString(),
                "Failed to start",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(exitCode: 1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                _logger?.LogInformation("Shutting down host.");
                await _host.StopAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                _host.Dispose();
                _host = null;
            }
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private static IHost BuildHost(InfrastructureOptions paths)
    {
        // Serilog bootstrap so even host-build failures get logged.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(paths.LogsFolder, "lamg-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                restrictedToMinimumLevel: LogEventLevel.Debug)
            .CreateBootstrapLogger();

        IHostBuilder builder = Host.CreateDefaultBuilder();

        builder.ConfigureAppConfiguration(config =>
        {
            string exeFolder = AppContext.BaseDirectory;
            config.SetBasePath(exeFolder);
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            config.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
            config.AddEnvironmentVariables("LAMG_");
        });

        builder.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    path: Path.Combine(paths.LogsFolder, "lamg-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    restrictedToMinimumLevel: LogEventLevel.Debug)
                .WriteTo.Sink(services.GetRequiredService<InMemoryLogSink>());
        });

        builder.ConfigureServices((context, services) =>
        {
            // Application + Infrastructure
            services.AddLamgApplication();
            services.AddLamgInfrastructure(opts =>
            {
                opts.DatabasePath = paths.DatabasePath;
                opts.LogsFolder = paths.LogsFolder;
                opts.DefaultOutputFolder = paths.DefaultOutputFolder;
                opts.FFmpegBundledFolder = paths.FFmpegBundledFolder;
            });

            // UI services
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // ViewModels (singleton so navigation preserves state)
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<ImportViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<ProcessingViewModel>();

            // Views
            services.AddSingleton<ShellWindow>();
        });

        return builder.Build();
    }

    private async void OnShellLoadedForCrashRecovery(object sender, RoutedEventArgs e)
    {
        // One-shot: unhook before doing async work so a window reopen
        // (or any future Loaded re-raise) cannot retrigger the prompt.
        if (sender is ShellWindow shell)
        {
            shell.Loaded -= OnShellLoadedForCrashRecovery;
        }

        try
        {
            await CheckResumableJobsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Crash recovery startup failed.");
        }
    }

    private async Task CheckResumableJobsAsync()
    {
        if (_host is null) return;

        ICrashRecoveryService recovery =
            _host.Services.GetRequiredService<ICrashRecoveryService>();

        IReadOnlyList<Job> jobs = await recovery
            .FindResumableJobsAsync()
            .ConfigureAwait(true);

        if (jobs.Count == 0)
        {
            _logger?.LogDebug("No resumable jobs found at startup.");
            return;
        }

        // The repository returns the freshest jobs first. Surface the
        // newest one; older interrupted jobs (if any) stay in the DB
        // and will reappear on a future launch.
        Job job = jobs[0];
        _logger?.LogInformation(
            "Found resumable job {JobId} (stage {Stage}); prompting user.",
            job.Id, job.CurrentStage);

        IDialogService dialog = _host.Services.GetRequiredService<IDialogService>();
        ResumeJobChoice choice = await dialog.ShowResumeJobAsync(job).ConfigureAwait(true);

        switch (choice)
        {
            case ResumeJobChoice.Resume:
                await ResumeJobAsync(job).ConfigureAwait(true);
                break;
            case ResumeJobChoice.Discard:
                await DiscardJobAsync(job, recovery).ConfigureAwait(true);
                break;
            default:
                _logger?.LogDebug(
                    "User cancelled resume prompt for job {JobId}; leaving it as-is.",
                    job.Id);
                break;
        }
    }

    private async Task ResumeJobAsync(Job job)
    {
        if (_host is null) return;

        INavigationService nav =
            _host.Services.GetRequiredService<INavigationService>();
        ProcessingViewModel processing =
            _host.Services.GetRequiredService<ProcessingViewModel>();
        IJobOrchestrator orchestrator =
            _host.Services.GetRequiredService<IJobOrchestrator>();
        IDialogService dialog =
            _host.Services.GetRequiredService<IDialogService>();

        // Switch to the Processing screen first so progress events
        // raised by ResumeAsync have somewhere to land.
        processing.AttachToJob(job.Id);
        nav.NavigateTo(AppPage.Processing);

        try
        {
            Progress<JobProgress> progress = new(processing.OnJobProgress);
            Result result = await orchestrator
                .ResumeAsync(job.Id, progress)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                _logger?.LogError("Resume failed for job {JobId}: {Error}", job.Id, result.Error);
                await dialog
                    .ShowErrorAsync("Resume failed", result.Error ?? "Unknown error")
                    .ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Resume threw for job {JobId}.", job.Id);
            await dialog.ShowErrorAsync("Resume failed", ex.Message).ConfigureAwait(true);
        }
    }

    private async Task DiscardJobAsync(Job job, ICrashRecoveryService recovery)
    {
        if (_host is null) return;

        // Step 1: delete any *.tmp render files left in the project's
        // output folder. Failure here is non-fatal; we still want to
        // mark the job as cancelled below.
        try
        {
            await recovery.CleanupOrphansAsync(job).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Orphan cleanup failed while discarding job {JobId}; continuing.",
                job.Id);
        }

        // Step 2: move the job out of the resumable set permanently.
        IJobRepository jobs = _host.Services.GetRequiredService<IJobRepository>();
        try
        {
            job.Status = JobStatus.Cancelled;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.LastHeartbeat = DateTimeOffset.UtcNow;
            await jobs.UpdateAsync(job).ConfigureAwait(true);
            _logger?.LogInformation("Discarded job {JobId}.", job.Id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to mark discarded job {JobId} as Cancelled.",
                job.Id);
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception.");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger?.LogError(ex, "Unhandled AppDomain exception (terminating={Terminating}).", e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
