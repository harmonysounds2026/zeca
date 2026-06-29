namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Measures EBU R128 integrated loudness and true peak using the
/// ffmpeg <c>ebur128</c> filter.
/// </summary>
public interface ILoudnessAnalyzer
{
    Task<LoudnessMeasurement> MeasureAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
