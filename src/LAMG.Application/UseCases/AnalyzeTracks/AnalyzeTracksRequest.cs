namespace LAMG.Application.UseCases.AnalyzeTracks;

/// <summary>
/// Inputs for the <see cref="AnalyzeTracksUseCase"/>.
/// </summary>
public sealed record AnalyzeTracksRequest(
    IReadOnlyList<long> BatchIds,
    double SilenceThresholdDb,
    int SilenceMinDurationMs,
    int MaxDegreeOfParallelism);
