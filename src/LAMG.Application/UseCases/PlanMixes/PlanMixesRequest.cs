using LAMG.Application.Settings;
using LAMG.Domain.Enums;

namespace LAMG.Application.UseCases.PlanMixes;

/// <summary>
/// Inputs for the <see cref="PlanMixesUseCase"/>. <see cref="Mode"/>
/// chooses which planner is invoked; <see cref="ReusePoolBatchIds"/>
/// is mandatory for <see cref="MixMode.Reuse"/> and ignored otherwise.
/// </summary>
public sealed record PlanMixesRequest(
    long ProjectId,
    MixMode Mode,
    AppSettings Settings,
    IReadOnlyCollection<long>? ReusePoolBatchIds);
