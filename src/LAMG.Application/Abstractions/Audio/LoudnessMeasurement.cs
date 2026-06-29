namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// EBU R128 measurement of a single track.
/// </summary>
public sealed record LoudnessMeasurement(
    double IntegratedLufs,
    double TruePeakDb);
