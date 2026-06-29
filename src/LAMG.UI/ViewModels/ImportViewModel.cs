using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LAMG.Application.Abstractions;
using LAMG.Application.Jobs;
using LAMG.Application.Settings;
using LAMG.Common;
using LAMG.UI.Services;

using Microsoft.Extensions.Logging;

namespace LAMG.UI.ViewModels;

/// <summary>
/// Backing for the Import screen. Hosts the chosen folders and starts
/// the Analyze-Import job through the orchestrator.
/// </summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly IDialogService _dialog;
    private readonly INavigationService _navigation;
    private readonly IJobOrchestrator _orchestrator;
    private readonly ISettingsService _settings;
    private readonly ProcessingViewModel _processing;
    private readonly ILogger<ImportViewModel> _logger;

    public ImportViewModel(
        IDialogService dialog,
        INavigationService navigation,
        IJobOrchestrator orchestrator,
        ISettingsService settings,
        ProcessingViewModel processing,
        ILogger<ImportViewModel> logger)
    {
        _dialog = Guard.NotNull(dialog);
        _navigation = Guard.NotNull(navigation);
        _orchestrator = Guard.NotNull(orchestrator);
        _settings = Guard.NotNull(settings);
        _processing = Guard.NotNull(processing);
        _logger = Guard.NotNull(logger);

        // Default the recursive flag from saved settings.
        Recursive = _settings.Current.ImportRecursively;
    }

    public ObservableCollection<BatchFolderRow> Folders { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private BatchFolderRow? _selectedFolder;

    [ObservableProperty]
    private bool _recursive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFoldersCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartAnalysisCommand))]
    private bool _isBusy;

    [RelayCommand(CanExecute = nameof(CanAddFolders))]
    private async Task AddFoldersAsync()
    {
        string? folder = await _dialog.ShowFolderPickerAsync().ConfigureAwait(true);
        if (folder is null) return;

        if (Folders.Any(f => string.Equals(f.Path, folder, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("Folder '{Folder}' is already in the import list.", folder);
            return;
        }

        Folders.Add(new BatchFolderRow { Path = folder });
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        if (SelectedFolder is not null)
        {
            Folders.Remove(SelectedFolder);
        }
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear() => Folders.Clear();

    [RelayCommand(CanExecute = nameof(CanStartAnalysis))]
    private async Task StartAnalysisAsync()
    {
        IsBusy = true;
        try
        {
            List<string> folderPaths = Folders.Select(f => f.Path).ToList();
            if (folderPaths.Count == 0)
            {
                return;
            }

            // Snapshot the current settings into the job; pick up the
            // user's latest "Recursive" preference from this screen.
            AppSettings current = _settings.Current;
            AppSettings snapshot = current with { ImportRecursively = Recursive };

            string projectName = "Project " +
                DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            JobRequest request = new(projectName, folderPaths, snapshot);
            Progress<JobProgress> progress = new(_processing.OnJobProgress);

            long jobId;
            try
            {
                jobId = await _orchestrator
                    .StartAsync(request, progress, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (InvalidOperationException ex)
            {
                await _dialog.ShowErrorAsync("Cannot start", ex.Message).ConfigureAwait(true);
                return;
            }

            _processing.AttachToJob(jobId);
            _navigation.NavigateTo(AppPage.Processing);

            _logger.LogInformation(
                "Job {JobId} started for project '{Project}' with {Count} folders.",
                jobId, projectName, folderPaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start analysis failed unexpectedly.");
            await _dialog.ShowErrorAsync("Start failed", ex.Message).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAddFolders() => !IsBusy;
    private bool CanRemoveSelected() => !IsBusy && SelectedFolder is not null;
    private bool CanClear() => !IsBusy && Folders.Count > 0;
    private bool CanStartAnalysis() => !IsBusy && Folders.Count > 0;
}
