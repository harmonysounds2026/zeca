using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using LAMG.Application.Abstractions;
using LAMG.Application.Settings;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.UI.Services;

using Microsoft.Extensions.Logging;

namespace LAMG.UI.ViewModels;

/// <summary>
/// Backing for the Settings screen. Mirrors every field on
/// <see cref="AppSettings"/> as an observable property so the UI can
/// bind directly. <see cref="SaveCommand"/> persists the values
/// through <see cref="ISettingsService"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        ISettingsService settings,
        IDialogService dialog,
        ILogger<SettingsViewModel> logger)
    {
        _settings = Guard.NotNull(settings);
        _dialog = Guard.NotNull(dialog);
        _logger = Guard.NotNull(logger);

        // Seed from whatever the SettingsService already loaded at
        // app startup (App.xaml.cs.OnStartup -> settings.LoadAsync()).
        // Falling back to defaults if the load hasn't run yet would
        // happen naturally because SettingsService.Current is seeded
        // with DefaultAppSettings.Value in its own constructor.
        // Previous code unconditionally used DefaultAppSettings.Value,
        // so a Save followed by a restart appeared to "lose" the
        // saved values - the VM was overwriting them on every
        // construction.
        ApplyFromSettings(_settings.Current);
    }

    // ---- Mirrored AppSettings fields ----

    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private int _targetDurationMinutes = 90;
    [ObservableProperty] private int _uniqueMixesPerBatch = 1;
    [ObservableProperty] private int _reuseMixCount;
    [ObservableProperty] private OutputFormat _outputFormat = OutputFormat.Mp3;
    [ObservableProperty] private int _mp3BitrateKbps = 192;
    [ObservableProperty] private int _wavBitDepth = 16;
    [ObservableProperty] private int _crossfadeMs = 1000;
    [ObservableProperty] private double _normalizationTargetLufs = -14.0;
    [ObservableProperty] private double _normalizationTruePeakDb = -1.5;
    [ObservableProperty] private double _silenceThresholdDb = -50.0;
    [ObservableProperty] private int _silenceMinDurationMs = 500;
    [ObservableProperty] private CpuMode _cpuMode = CpuMode.Normal;

    // Field deliberately spelled with both 'F's uppercase so the
    // CommunityToolkit.Mvvm source generator emits the property as
    // "FFmpegPathOverride" (matching AppSettings) rather than the
    // default "FfmpegPathOverride" it would produce from "_ffmpeg...".
    [ObservableProperty] private string? _FFmpegPathOverride;
    [ObservableProperty] private bool _importRecursively;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ---- Static helpers for combo binding ----

    public IReadOnlyList<int> TargetDurationOptions { get; } = [60, 90, 120];

    public IReadOnlyList<OutputFormat> OutputFormatOptions { get; }
        = [OutputFormat.Mp3, OutputFormat.Wav];

    public IReadOnlyList<int> Mp3BitrateOptions { get; } = [128, 160, 192, 256, 320];

    public IReadOnlyList<int> WavBitDepthOptions { get; } = [16, 24];

    public IReadOnlyList<CpuMode> CpuModeOptions { get; }
        = [CpuMode.Eco, CpuMode.Normal, CpuMode.High];

    // ---- Commands ----

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BrowseOutputFolderAsync()
    {
        string? folder = await _dialog
            .ShowFolderPickerAsync(OutputFolder)
            .ConfigureAwait(true);

        if (!string.IsNullOrEmpty(folder))
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BrowseFFmpegFolderAsync()
    {
        string? folder = await _dialog
            .ShowFolderPickerAsync(FFmpegPathOverride)
            .ConfigureAwait(true);

        if (!string.IsNullOrEmpty(folder))
        {
            FFmpegPathOverride = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task LoadAsync()
    {
        try
        {
            IsBusy = true;
            AppSettings loaded = await _settings.LoadAsync().ConfigureAwait(true);
            ApplyFromSettings(loaded);
            StatusMessage = "Loaded.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SettingsViewModel.LoadAsync failed.");
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task SaveAsync()
    {
        try
        {
            IsBusy = true;
            AppSettings snapshot = BuildSnapshot();
            await _settings.SaveAsync(snapshot).ConfigureAwait(true);
            StatusMessage = "Saved.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SettingsViewModel.SaveAsync failed.");
            StatusMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void ResetToDefaults() => ApplyFromSettings(DefaultAppSettings.Value);

    private bool CanInteract() => !IsBusy;

    private void ApplyFromSettings(AppSettings s)
    {
        OutputFolder = s.OutputFolder;
        TargetDurationMinutes = s.TargetDurationMinutes;
        UniqueMixesPerBatch = s.UniqueMixesPerBatch;
        ReuseMixCount = s.ReuseMixCount;
        OutputFormat = s.OutputFormat;
        Mp3BitrateKbps = s.Mp3BitrateKbps;
        WavBitDepth = s.WavBitDepth;
        CrossfadeMs = s.CrossfadeMs;
        NormalizationTargetLufs = s.NormalizationTargetLufs;
        NormalizationTruePeakDb = s.NormalizationTruePeakDb;
        SilenceThresholdDb = s.SilenceThresholdDb;
        SilenceMinDurationMs = s.SilenceMinDurationMs;
        CpuMode = s.CpuMode;
        FFmpegPathOverride = s.FFmpegPathOverride;
        ImportRecursively = s.ImportRecursively;
    }

    private AppSettings BuildSnapshot() => new()
    {
        OutputFolder = OutputFolder,
        TargetDurationMinutes = TargetDurationMinutes,
        UniqueMixesPerBatch = UniqueMixesPerBatch,
        ReuseMixCount = ReuseMixCount,
        OutputFormat = OutputFormat,
        Mp3BitrateKbps = Mp3BitrateKbps,
        WavBitDepth = WavBitDepth,
        CrossfadeMs = CrossfadeMs,
        NormalizationTargetLufs = NormalizationTargetLufs,
        NormalizationTruePeakDb = NormalizationTruePeakDb,
        SilenceThresholdDb = SilenceThresholdDb,
        SilenceMinDurationMs = SilenceMinDurationMs,
        CpuMode = CpuMode,
        FFmpegPathOverride = FFmpegPathOverride,
        ImportRecursively = ImportRecursively,
    };
}
