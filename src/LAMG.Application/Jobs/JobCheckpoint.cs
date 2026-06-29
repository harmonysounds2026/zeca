using LAMG.Domain.Enums;

namespace LAMG.Application.Jobs;

/// <summary>
/// Serialised into <see cref="LAMG.Domain.Models.Job.PayloadJson"/> on
/// every meaningful step. Carries enough information to resume the
/// job at the exact same place after a crash or power loss.
/// </summary>
public sealed record JobCheckpoint(
    long ProjectId,
    JobStage Stage,
    IReadOnlyList<long> BatchIds,
    IReadOnlyList<long>? ReusePoolBatchIds,
    int UniqueMixesCompleted,
    int ReuseMixesCompleted,
    int TracksSkipped);
