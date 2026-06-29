using LAMG.Application.Settings;
using LAMG.Application.UseCases.PlanMixes;
using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Planning;

/// <summary>
/// Plans <see cref="LAMG.Domain.Enums.MixMode.Unique"/> mixes.
/// One <see cref="PlannedMix"/> is produced per imported batch.
/// Tracks are not shared across batches in this mode.
/// </summary>
public interface IUniqueModePlanner
{
    /// <summary>
    /// Produces a planned mix for the given batch using only tracks
    /// belonging to that batch. If the available pool cannot reach the
    /// target duration, the returned plan is simply shorter.
    /// </summary>
    /// <param name="batch">The batch to plan a mix for.</param>
    /// <param name="batchTracks">Ready tracks that belong to <paramref name="batch"/>.</param>
    /// <param name="settings">The settings snapshot of the running project.</param>
    /// <param name="indexInProject">1-based mix index to assign.</param>
    PlannedMix Plan(
        Batch batch,
        IReadOnlyList<Track> batchTracks,
        AppSettings settings,
        int indexInProject);
}
