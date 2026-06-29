namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Detects leading and trailing silence in an audio file by parsing the
/// output of the ffmpeg <c>silencedetect</c> filter.
/// </summary>
public interface ISilenceDetector
{
    /// <param name="filePath">Absolute path to the audio file.</param>
    /// <param name="noiseFloorDb">
    /// Silence threshold in dBFS (e.g. <c>-50</c>). Samples below this
    /// level are considered silent.
    /// </param>
    /// <param name="minSilenceMs">
    /// Minimum contiguous silence to report, in milliseconds.
    /// </param>
    Task<SilenceMeasurement> DetectAsync(
        string filePath,
        double noiseFloorDb,
        int minSilenceMs,
        CancellationToken cancellationToken = default);
}
