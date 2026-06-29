using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.System;
using LAMG.Application.Jobs;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.UI.Services;

using Microsoft.Extensions.Logging;

namespace LAMG.UI.ViewModels;

/// <summary>
/// Backing for the Processing screen. Subscribes to the orchestrator's
/// lifecycle and duplicate-resolution events, mirrors the in-memory
/// Serilog ring buffer into the live-log list, and wires the
/// Pause/Resume/Cancel commands to <see cref="IJobOrchestrator"/>.
/// </summary>
/// <remarks>
/// Every reference to the WPF <c>Application</c> class is fully
/// qualified as <c>System.Windows.Application</c> because the
/// sibling <c>LAMG.Application</c> namespace shadows the unqualified
/// name inside this assembly.
/// </remarks>
public sealed partial class ProcessingViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 5000;

    private readonly IJobOrchestrator _orchestrator;
    private readonly ILogReader _logReader;
    private readonly IDialogService _dialog;
    private readonly INavigationService _navigation;
    private readonly ILogger<ProcessingViewModel> _logger;

    private readonly EventHandler<LogLine> _logHandler;
    private readonly EventHandler<JobLifecycleEvent> _lifecycleHandler;
    private readonly EventHandler<DuplicateResolutionRequestedEventArgs> _duplicateHandler;
    private readonly EventHandler<ReusePoolRequestedEventArgs> _reusePoolHandler;
    private readonly EventHandler<MixRenderedEventArgs> _mixRenderedHandler;

    private bool _disposed;

    public ProcessingViewModel(
        IJobOrchestrator orchestrator,
        ILogReader logReader,
        IDialogService dialog,
        INavigationService navigation,
        ILogger<ProcessingViewModel> logger)
    {
        _orchestrator = Guard.NotNull(orchestrator);
        _logReader = Guard.NotNull(logReader);
        _dialog = Guard.NotNull(dialog);
        _navigation = Guard.NotNull(navigation);
        _logger = Guard.NotNull(logger);

        // Seed the visible log with whatever is already in the ring buffer.
        foreach (LogLine line in _logReader.Snapshot())
        {
            LogLines.Add(line);
        }

        _logHandler = OnLogLineWritten;
        _lifecycleHandler = OnJobLifecycleChanged;
        _duplicateHandler = OnDuplicateResolutionRequested;
        _reusePoolHandler = OnReusePoolRequested;
        _mixRenderedHandler = OnMixRendered;

        _logReader.LineWritten += _logHandler;
        _orchestrator.LifecycleChanged += _lifecycleHandler;
        _orchestrator.DuplicateResolutionRequested += _duplicateHandler;
        _orchestrator.ReusePoolRequested += _reusePoolHandler;
        _orchestrator.MixRendered += _mixRenderedHandler;
    }

    public ObservableCollection<LogLine> LogLines { get; } = [];

    public ObservableCollection<CompletedMixRow> CompletedMixes { get; } = [];

    [ObservableProperty] private string _stageDescription = "Idle.";
    [ObservableProperty] private int _mixesCompleted;
    [ObservableProperty] private int _mixesPlanned;
    [ObservableProperty] private int _tracksSkipped;
    [ObservableProperty] private double _overallFraction;
    [ObservableProperty] private string? _currentMixName;
    [ObservableProperty] private TimeSpan _elapsed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private long? _activeJobId;

    // ============================================================
    // Public surface used by ImportViewModel
    // ============================================================

    /// <summary>
    /// Invoked by the orchestrator's <see cref="IProgress{T}"/> sink.
    /// <c>Progress&lt;T&gt;</c> captures the UI sync context at
    /// construction in ImportViewModel, so this callback always runs
    /// on the UI thread.
    /// </summary>
    public void OnJobProgress(JobProgress p)
    {
        StageDescription = p.StageDescription;
        OverallFraction = p.OverallFraction;
        MixesCompleted = p.MixesCompleted;
        MixesPlanned = p.MixesPlanned;
        TracksSkipped = p.TracksSkipped;
        CurrentMixName = p.CurrentMixName;
        Elapsed = p.Elapsed;
    }

    /// <summary>Called by ImportViewModel right after a new job starts.</summary>
    public void AttachToJob(long jobId)
    {
        ActiveJobId = jobId;
        IsRunning = true;
        StageDescription = "Starting...";
        OverallFraction = 0;
        MixesCompleted = 0;
        MixesPlanned = 0;
        TracksSkipped = 0;
        CurrentMixName = null;
        Elapsed = TimeSpan.Zero;
        CompletedMixes.Clear();
    }

    // ============================================================
    // Commands
    // ============================================================

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        // Starting a brand-new job happens on the Import screen.
        _navigation.NavigateTo(AppPage.Import);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        if (ActiveJobId is not long jobId) return;
        try
        {
            await _orchestrator.PauseAsync(jobId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pause failed.");
            await _dialog.ShowErrorAsync("Pause failed", ex.Message).ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        if (ActiveJobId is not long jobId) return;
        try
        {
            Progress<JobProgress> progress = new(OnJobProgress);
            Result result = await _orchestrator
                .ResumeAsync(jobId, progress)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                await _dialog
                    .ShowErrorAsync("Resume failed", result.Error ?? "Unknown error")
                    .ConfigureAwait(true);
                return;
            }

            IsRunning = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume failed.");
            await _dialog.ShowErrorAsync("Resume failed", ex.Message).ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        if (ActiveJobId is not long jobId) return;
        try
        {
            await _orchestrator.CancelAsync(jobId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cancel failed.");
            await _dialog.ShowErrorAsync("Cancel failed", ex.Message).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ClearLogs() => LogLines.Clear();

    private bool CanStart() => !IsRunning;
    private bool CanPause() => IsRunning && ActiveJobId.HasValue;
    private bool CanResume() => !IsRunning && ActiveJobId.HasValue;
    private bool CanCancel() => ActiveJobId.HasValue;

    // ============================================================
    // Event handlers (background thread → UI dispatcher)
    // ============================================================

    private void OnLogLineWritten(object? sender, LogLine line)
    {
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is null) return;

        app.Dispatcher.BeginInvoke(() =>
        {
            if (LogLines.Count >= MaxLogLines)
            {
                LogLines.RemoveAt(0);
            }
            LogLines.Add(line);
        });
    }

    private void OnJobLifecycleChanged(object? sender, JobLifecycleEvent e)
    {
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is null) return;

        app.Dispatcher.BeginInvoke(() =>
        {
            string desc = $"{e.Stage} - {e.Status}";
            if (!string.IsNullOrEmpty(e.Message))
            {
                desc = $"{desc}: {e.Message}";
            }
            StageDescription = desc;

            switch (e.Status)
            {
                case JobStatus.Running:
                    IsRunning = true;
                    break;
                case JobStatus.Paused:
                    IsRunning = false;
                    break;
                case JobStatus.Completed:
                    IsRunning = false;
                    ActiveJobId = null;
                    OverallFraction = 1.0;
                    break;
                case JobStatus.Failed:
                    IsRunning = false;
                    ActiveJobId = null;
                    // Fire-and-forget so we don't block the lifecycle
                    // handler. The message comes from the orchestrator's
                    // exception text; null is unexpected but defended.
                    string failMsg = string.IsNullOrEmpty(e.Message)
                        ? "The job failed with an unknown error. See the log for details."
                        : e.Message!;
                    _ = _dialog.ShowErrorAsync("Job failed", failMsg);
                    break;
                case JobStatus.Cancelled:
                    IsRunning = false;
                    ActiveJobId = null;
                    break;
            }
        });
    }

    private void OnDuplicateResolutionRequested(
        object? sender,
        DuplicateResolutionRequestedEventArgs e)
    {
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is null) return;

        // Fire-and-forget on the UI dispatcher: show the dialog, then
        // submit the user's choice back to the orchestrator. The
        // orchestrator's work task is awaiting that submission via TCS.
        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            DuplicateResolution resolution = DuplicateResolution.ImportAll;
            try
            {
                DuplicateResolution? choice = await _dialog
                    .ShowDuplicateResolutionAsync(e.Report)
                    .ConfigureAwait(true);
                resolution = choice ?? DuplicateResolution.ImportAll;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Duplicate resolution dialog failed; defaulting to ImportAll.");
            }

            try
            {
                await _orchestrator
                    .SubmitDuplicateResolutionAsync(e.JobId, resolution)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Submitting duplicate resolution failed for job {JobId}.",
                    e.JobId);
            }
        });
    }

    private void OnReusePoolRequested(
        object? sender,
        ReusePoolRequestedEventArgs e)
    {
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is null) return;

        // Same pattern as the duplicate dialog: marshal to the UI
        // dispatcher, show the modal, then submit the result back
        // to the orchestrator (which is awaiting the TCS).
        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            IReadOnlyCollection<long> selection = Array.Empty<long>();
            try
            {
                IReadOnlyCollection<long>? choice = await _dialog
                    .ShowReusePoolSelectionAsync(e.Batches)
                    .ConfigureAwait(true);

                // null  = user cancelled  -> skip reuse-mode entirely
                // empty = user opted out  -> same effect
                // set   = user's picks    -> orchestrator plans reuse mixes
                selection = choice ?? Array.Empty<long>();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Reuse pool dialog failed; skipping reuse-mode for this job.");
            }

            try
            {
                await _orchestrator
                    .SubmitReusePoolAsync(e.JobId, selection)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Submitting reuse pool failed for job {JobId}.",
                    e.JobId);
            }
        });
    }

    private void OnMixRendered(object? sender, MixRenderedEventArgs e)
    {
        System.Windows.Application? app = System.Windows.Application.Current;
        if (app is null) return;

        // Build the row on the calling thread (immutable values only),
        // then marshal to the UI thread to append.
        string fileName = !string.IsNullOrEmpty(e.OutputPath)
            ? Path.GetFileName(e.OutputPath!)
            : $"(failed) mix_{e.IndexInProject:000}";

        CompletedMixRow row = new()
        {
            MixId = e.MixId,
            IndexInProject = e.IndexInProject,
            Mode = e.Mode,
            FileName = fileName,
            OutputPath = e.OutputPath ?? string.Empty,
            ActualDurationSec = e.ActualDurationSeconds,
            OutputFormat = e.OutputFormat,
            Status = e.Status,
        };

        app.Dispatcher.BeginInvoke(() => CompletedMixes.Add(row));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _logReader.LineWritten -= _logHandler;
        _orchestrator.LifecycleChanged -= _lifecycleHandler;
        _orchestrator.DuplicateResolutionRequested -= _duplicateHandler;
        _orchestrator.ReusePoolRequested -= _reusePoolHandler;
        _orchestrator.MixRendered -= _mixRenderedHandler;
        _disposed = true;
    }
}
