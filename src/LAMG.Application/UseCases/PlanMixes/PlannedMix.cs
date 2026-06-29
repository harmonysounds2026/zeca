using LAMG.Domain.Models;

namespace LAMG.Application.UseCases.PlanMixes;

/// <summary>
/// Output of a planner. The <see cref="Mix"/> object is filled in but
/// not yet persisted; the <see cref="Items"/> reference real
/// <see cref="Track"/> ids; <see cref="SourceBatchIds"/> describes which
/// batches contributed tracks to this mix (one id for Unique mode, one
/// or more for Reuse mode).
/// </summary>
public sealed record PlannedMix(
    Mix Mix,
    IReadOnlyList<MixItem> Items,
    IReadOnlyCollection<long> SourceBatchIds);
