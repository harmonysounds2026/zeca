namespace LAMG.Application.Settings;

/// <summary>
/// Canonical keys used in the <c>Settings</c> table. Kept centralised
/// so misspellings cause compile errors.
/// </summary>
public static class SettingsKeys
{
    /// <summary>
    /// Single key under which the entire <see cref="AppSettings"/> record
    /// is stored as a JSON blob. Using one blob lets settings evolve
    /// without DB migrations; the individual keys below remain available
    /// for future per-field updates.
    /// </summary>
    public const string AppSettingsJson = "app.settings.json";

    public const string OutputFolder = "output.folder";
    public const string TargetDurationMinutes = "mix.targetMinutes";
    public const string UniqueMixesPerBatch = "mix.unique.perBatch";
    public const string ReuseMixCount = "mix.reuse.count";
    public const string OutputFormat = "output.format";
    public const string Mp3BitrateKbps = "output.mp3.bitrateKbps";
    public const string WavBitDepth = "output.wav.bitDepth";
    public const string CrossfadeMs = "mix.crossfadeMs";
    public const string NormalizationTargetLufs = "audio.norm.targetLufs";
    public const string NormalizationTruePeakDb = "audio.norm.truePeakDb";
    public const string SilenceThresholdDb = "audio.silence.thresholdDb";
    public const string SilenceMinDurationMs = "audio.silence.minDurationMs";
    public const string CpuMode = "perf.cpuMode";
    public const string FFmpegPathOverride = "ffmpeg.pathOverride";
    public const string ImportRecursively = "import.recursive";
}
