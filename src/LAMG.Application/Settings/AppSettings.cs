using LAMG.Domain.Enums;

namespace LAMG.Application.Settings;

/// <summary>
/// Strongly-typed user settings persisted under <c>SettingScope.User</c>
/// and snapshotted to <c>Project.SettingsJson</c> when a project starts.
/// </summary>
/// <remarks>
/// Settings edited while a job is running do not affect that job. They
/// take effect on the next job.
/// </remarks>
public sealed record AppSettings
{
    /// <summary>Absolute folder where rendered mixes are written.</summary>
    public string OutputFolder { get; init; } = string.Empty;

    /// <summary>Target mix duration in minutes (60, 90, 120).</summary>
    public int TargetDurationMinutes { get; init; } = 90;

    /// <summary>How many Unique-mode mixes to generate per batch (typically 1).</summary>
    public int UniqueMixesPerBatch { get; init; } = 1;

    /// <summary>How many Reuse-mode mixes to generate in total.</summary>
    public int ReuseMixCount { get; init; }

    public OutputFormat OutputFormat { get; init; } = OutputFormat.Mp3;

    /// <summary>MP3 constant bitrate when <see cref="OutputFormat"/> is MP3.</summary>
    public int Mp3BitrateKbps { get; init; } = 192;

    /// <summary>WAV bit depth (16 or 24) when <see cref="OutputFormat"/> is WAV.</summary>
    public int WavBitDepth { get; init; } = 16;

    /// <summary>Crossfade between adjacent tracks in milliseconds.</summary>
    public int CrossfadeMs { get; init; } = 1000;

    /// <summary>EBU R128 loudnorm target loudness in LUFS.</summary>
    public double NormalizationTargetLufs { get; init; } = -14.0;

    /// <summary>Loudnorm true-peak ceiling in dBTP.</summary>
    public double NormalizationTruePeakDb { get; init; } = -1.5;

    /// <summary>Silence detection threshold in dBFS.</summary>
    public double SilenceThresholdDb { get; init; } = -50.0;

    /// <summary>Minimum contiguous silence to detect, in milliseconds.</summary>
    public int SilenceMinDurationMs { get; init; } = 500;

    public CpuMode CpuMode { get; init; } = CpuMode.Normal;

    /// <summary>
    /// When set, overrides the default ffmpeg discovery order.
    /// Must point at a folder containing <c>ffmpeg(.exe)</c> and
    /// <c>ffprobe(.exe)</c>.
    /// </summary>
    public string? FFmpegPathOverride { get; init; }

    /// <summary>
    /// When true, batch import recursively scans subdirectories.
    /// </summary>
    public bool ImportRecursively { get; init; }
}
