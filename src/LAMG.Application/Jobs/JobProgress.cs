using LAMG.Domain.Enums;

namespace LAMG.Application.Jobs;

/// <summary>
/// Snapshot of the orchestrator's progress at a point in time. Pushed
/// to UI through <see cref="IProgress{T}"/>.
/// </summary>
public sealed record JobProgress(
    JobStage Stage,
    string StageDescription,
    int MixesCompleted,
    int MixesPlanned,
    int TracksSkipped,
    string? CurrentMixName,
    double OverallFraction,
    TimeSpan Elapsed);
