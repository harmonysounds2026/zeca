using LAMG.Application.Abstractions.Planning;
using LAMG.Application.Settings;
using LAMG.Application.UseCases.PlanMixes;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.Planning;

/// <summary>
/// Plans Unique-mode mixes: exactly one mix per batch, tracks drawn
/// only from that batch, no repetition inside the mix. If the batch's
/// total effective duration is below the target, the mix is simply
/// shorter; if it is above, we stop on the first track that pushes the
/// running total over the target (so the result may slightly overshoot).
/// </summary>
/// <remarks>
/// Track order is preserved from the input list. The repository hands
/// tracks back ordered by <c>file_name ASC, id ASC</c>, so the mix
/// plays in filename order with import order (the monotonic row id)
/// as the tiebreaker for identical filenames. Unique Mode never
/// shuffles — that is reserved for Reuse Mode where the goal is
/// variation, not faithful playback.
/// </remarks>
public sealed class UniqueModePlanner : IUniqueModePlanner
{
    /// <summary>
    /// Tracks whose effective duration (after silence trim) is below
    /// this threshold are skipped: too short to crossfade cleanly.
    /// </summary>
    private const int MinTrackEffectiveMs = 200;

    private readonly ILogger<UniqueModePlanner> _logger;

    public UniqueModePlanner(ILogger<UniqueModePlanner> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public PlannedMix Plan(
        Batch batch,
        IReadOnlyList<Track> batchTracks,
        AppSettings settings,
        int indexInProject)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(batchTracks);
        ArgumentNullException.ThrowIfNull(settings);
        Guard.Positive(indexInProject);

        long targetMs = Math.Max(1, settings.TargetDurationMinutes) * 60_000L;
        int defaultCrossfadeMs = Math.Max(0, settings.CrossfadeMs);

        // Filter preserves the caller's order (filename ASC, id ASC
        // from the repository). No shuffle: Unique Mode plays back
        // the batch in its natural order.
        List<Track> eligible = FilterEligible(batchTracks);
        if (eligible.Count == 0)
        {
            _logger.LogInformation(
                "Batch {BatchId}: no eligible Ready tracks; producing empty Unique mix.",
                batch.Id);
            return BuildPlan(batch, indexInProject, settings, items: [], totalMs: 0);
        }

        // Greedy fill until target reached or slightly exceeded.
        List<MixItem> items = new(eligible.Count);
        long accumulator = 0;
        long? prevEffectiveMs = null;

        foreach (Track t in eligible)
        {
            long thisEff = Math.Max(0, t.EffectiveDurationMs);

            int xfadeIn = 0;
            if (prevEffectiveMs is long prev)
            {
                long shorter = Math.Min(prev, thisEff);
                xfadeIn = (int)Math.Min(defaultCrossfadeMs, shorter / 2);
            }

            // Stitch the new track to the previous one.
            if (items.Count > 0)
            {
                items[^1].XfadeOutMs = xfadeIn;
            }

            items.Add(new MixItem
            {
                TrackId = t.Id,
                OrderIndex = items.Count,
                TrimmedMs = (int)Math.Min(int.MaxValue, thisEff),
                XfadeInMs = xfadeIn,
                XfadeOutMs = 0,
            });

            accumulator += thisEff - xfadeIn;
            prevEffectiveMs = thisEff;

            if (accumulator >= targetMs)
            {
                break;
            }
        }

        _logger.LogDebug(
            "Unique plan for batch {BatchId} #{Index}: {Count} tracks, {Sec}s (target {TargetSec}s, ordered).",
            batch.Id, indexInProject, items.Count, accumulator / 1000, targetMs / 1000);

        return BuildPlan(batch, indexInProject, settings, items, accumulator);
    }

    private static List<Track> FilterEligible(IReadOnlyList<Track> tracks)
    {
        List<Track> result = new(tracks.Count);
        foreach (Track t in tracks)
        {
            if (t.Status != TrackStatus.Ready) continue;
            if (t.EffectiveDurationMs < MinTrackEffectiveMs) continue;
            result.Add(t);
        }
        return result;
    }

    private static PlannedMix BuildPlan(
        Batch batch,
        int indexInProject,
        AppSettings settings,
        List<MixItem> items,
        long totalMs)
    {
        Mix mix = new()
        {
            ProjectId = batch.ProjectId,
            IndexInProject = indexInProject,
            TargetMin = Math.Max(0, settings.TargetDurationMinutes),
            ActualSec = (int)Math.Min(int.MaxValue, totalMs / 1000),
            Mode = MixMode.Unique,
            OutputFormat = settings.OutputFormat,
            OutputPath = null,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = MixStatus.Planned,
        };

        return new PlannedMix(mix, items, new[] { batch.Id });
    }
}
