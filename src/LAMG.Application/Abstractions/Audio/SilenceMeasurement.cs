namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Output of the silence detector for a single track. <c>SilenceLeadMs</c>
/// is the contiguous silence at the start; <c>SilenceTailMs</c> is the
/// contiguous silence at the end. Internal silences are not tracked.
/// </summary>
public sealed record SilenceMeasurement(
    int SilenceLeadMs,
    int SilenceTailMs);
