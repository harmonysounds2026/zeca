using LAMG.Domain.Enums;

namespace LAMG.Domain.Models;

/// <summary>
/// A single source audio file discovered in a batch. Populated
/// progressively as the analysis pipeline runs against it.
/// </summary>
public sealed class Track
{
    public long Id { get; set; }

    public long BatchId { get; set; }

    /// <summary>Absolute path on disk.</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>File name without directory components.</summary>
    public string FileName { get; set; } = string.Empty;

    public AudioFormat Format { get; set; }

    public long FileSizeBytes { get; set; }

    public long DurationMs { get; set; }

    public int SampleRate { get; set; }

    public int Channels { get; set; }

    public int? BitrateKbps { get; set; }

    /// <summary>
    /// SHA-256 hex of the raw file bytes. Cheap pre-filter for duplicates.
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex of the decoded PCM stream. Catches duplicates that
    /// share audio content but differ in container or encoding.
    /// </summary>
    public string AudioHash { get; set; } = string.Empty;

    public int SilenceLeadMs { get; set; }

    public int SilenceTailMs { get; set; }

    /// <summary>EBU R128 integrated loudness in LUFS.</summary>
    public double? IntegratedLufs { get; set; }

    /// <summary>True peak in dBTP.</summary>
    public double? TruePeakDb { get; set; }

    public TrackStatus Status { get; set; } = TrackStatus.Pending;

    public int TimesUsed { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// Effective playable duration after silence trimming.
    /// Computed; never persisted.
    /// </summary>
    public long EffectiveDurationMs
        => Math.Max(0, DurationMs - SilenceLeadMs - SilenceTailMs);
}
