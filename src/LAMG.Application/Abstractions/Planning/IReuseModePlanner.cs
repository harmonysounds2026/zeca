using LAMG.Application.Settings;
using LAMG.Application.UseCases.PlanMixes;
using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Planning;

/// <summary>
/// Plans <see cref="LAMG.Domain.Enums.MixMode.Reuse"/> mixes. Tracks
/// may be reused across mixes. The candidate pool is drawn from the
/// caller-supplied set of batches; the planner never picks tracks
/// from outside this pool.
/// </summary>
public interface IReuseModePlanner
{
    /// <summary>
    /// Produces <paramref name="mixesToGenerate"/> reuse mixes whose
    /// track set comes only from <paramref name="poolTracks"/>. Uses
    /// basic count-based overlap avoidance against the supplied
    /// <paramref name="priorMixes"/>.
    /// </summary>
    /// <param name="poolTracks">Ready tracks from the user-selected reuse batches.</param>
    /// <param name="priorMixes">
    /// Track sets of previously planned mixes (Unique and Reuse) used
    /// for overlap avoidance.
    /// </param>
    /// <param name="settings">The active project settings snapshot.</param>
    /// <param name="firstIndexInProject">1-based mix index assigned to the first reuse mix.</param>
    /// <param name="reusePoolBatchIds">Batch ids that contributed tracks to the pool.</param>
    /// <param name="mixesToGenerate">Total reuse mixes to plan.</param>
    /// <param name="projectId">Project the mixes belong to (written into <see cref="Mix.ProjectId"/>).</param>
    IReadOnlyList<PlannedMix> Plan(
        IReadOnlyList<Track> poolTracks,
        IReadOnlyList<IReadOnlyCollection<long>> priorMixes,
        IReadOnlyCollection<long> reusePoolBatchIds,
        AppSettings settings,
        int firstIndexInProject,
        int mixesToGenerate,
        long projectId);
}
