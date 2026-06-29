using LAMG.Domain.Enums;

namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Structural metadata about an audio file, read by ffprobe. The
/// values are derived from the container; they say nothing about the
/// audio content itself.
/// </summary>
public sealed record ProbeResult(
    AudioFormat Format,
    long DurationMs,
    int SampleRate,
    int Channels,
    int? BitrateKbps,
    long FileSizeBytes);
