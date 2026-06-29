using LAMG.Application.Abstractions.Planning;
using LAMG.Application.Settings;
using LAMG.Application.UseCases.PlanMixes;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.Planning;

/// <summary>
/// Plans Reuse-mode mixes from the user-selected batch pool. Tracks
/// may be reused across mixes (never within a single mix); the planner
/// prefers least-used tracks first so usage spreads evenly, and uses a
/// simple overlap avoidance pass to keep new mixes from looking like
/// near-copies of earlier ones.
/// </summary>
/// <remarks>
/// Overlap avoidance: for each candidate mix, we compute
/// <c>|candidate ∩ prior| / |candidate|</c> against every prior mix
/// (Unique + earlier Reuse). If the largest such ratio exceeds
/// <see cref="OverlapThreshold"/>, we reshuffle with a different seed
/// and retry, up to <see cref="MaxOverlapAttempts"/> times. After that
/// the best attempt so far is accepted with a warning log — never
/// silently abandoning a mix.
/// </remarks>
public sealed class ReuseModePlanner : IReuseModePlanner
{
    private const int MinTrackEffectiveMs = 200;
    private const int MaxOverlapAttempts = 3;
    private const double OverlapThreshold = 0.70;

    private readonly ILogger<ReuseModePlanner> _logger;

    public ReuseModePlanner(ILogger<ReuseModePlanner> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public IReadOnlyList<PlannedMix> Plan(
        IReadOnlyList<Track> poolTracks,
        IReadOnlyList<IReadOnlyCollection<long>> priorMixes,
        IReadOnlyCollection<long> reusePoolBatchIds,
        AppSettings settings,
        int firstIndexInProject,
        int mixesToGenerate,
        long projectId)
    {
        ArgumentNullException.ThrowIfNull(poolTracks);
        ArgumentNullException.ThrowIfNull(priorMixes);
        ArgumentNullException.ThrowIfNull(reusePoolBatchIds);
        ArgumentNullException.ThrowIfNull(settings);
        Guard.Positive(firstIndexInProject);
        Guard.NotNegative(mixesToGenerate);
        Guard.Positive(projectId);

        if (mixesToGenerate == 0)
        {
            return Array.Empty<PlannedMix>();
        }

        List<Track> eligible = FilterEligible(poolTracks);
        if (eligible.Count == 0)
        {
            _logger.LogWarning(
                "Reuse planner: pool of {Count} tracks has zero eligible tracks; no reuse mixes generated.",
                poolTracks.Count);
            return Array.Empty<PlannedMix>();
        }

        long[] sourceBatchIds = reusePoolBatchIds.ToArray();

        // Track-id sets of every prior mix, plus those we generate now.
        List<HashSet<long>> allPriorSets = new(priorMixes.Count + mixesToGenerate);
        foreach (IReadOnlyCollection<long> prior in priorMixes)
        {
            allPriorSets.Add(new HashSet<long>(prior));
        }

        List<PlannedMix> result = new(mixesToGenerate);

        for (int k = 0; k < mixesToGenerate; k++)
        {
            int currentIndex = firstIndexInProject + k;
            PlannedMix? best = null;
            HashSet<long>? bestSet = null;
            double bestOverlap = double.MaxValue;

            for (int attempt = 0; attempt < MaxOverlapAttempts; attempt++)
            {
                int seed = ComputeSeed(projectId, currentIndex, attempt);
                PlannedMix candidate = BuildOneMix(
                    eligible, settings, currentIndex, seed, sourceBatchIds, projectId);

                if (candidate.Items.Count == 0)
                {
                    // Pool is effectively empty; further attempts won't help.
                    break;
                }

                HashSet<long> candidateSet = ToTrackSet(candidate);
                double maxOverlap = ComputeMaxOverlap(candidateSet, allPriorSets);

                if (maxOverlap <= OverlapThreshold)
                {
                    best = candidate;
                    bestSet = candidateSet;
                    bestOverlap = maxOverlap;
                    break;
                }

                if (maxOverlap < bestOverlap)
                {
                    best = candidate;
                    bestSet = candidateSet;
                    bestOverlap = maxOverlap;
                }
            }

            if (best is null || bestSet is null)
            {
                _logger.LogWarning(
                    "Reuse mix #{Index}: no candidate produced (pool empty after filtering); stopping.",
                    currentIndex);
                break;
            }

            if (bestOverlap > OverlapThreshold)
            {
                _logger.LogWarning(
                    "Reuse mix #{Index}: best attempt has {Overlap:P0} overlap (> {Threshold:P0}); accepting anyway.",
                    currentIndex, bestOverlap, OverlapThreshold);
            }
            else
            {
                _logger.LogDebug(
                    "Reuse mix #{Index}: accepted with {Overlap:P0} overlap, {Count} tracks.",
                    currentIndex, bestOverlap, best.Items.Count);
            }

            result.Add(best);
            allPriorSets.Add(bestSet);
        }

        return result;
    }

    // ---------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------

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

    private static PlannedMix BuildOneMix(
        IReadOnlyList<Track> pool,
        AppSettings settings,
        int indexInProject,
        int seed,
        IReadOnlyCollection<long> sourceBatchIds,
        long projectId)
    {
        Random rng = new(seed);

        // Least-used-first; deterministic random tiebreak inside each
        // usage bucket. Each track gets one key from this RNG so the
        // ordering is fully determined by (projectId, index, attempt).
        List<(Track Track, int Key)> keyed = new(pool.Count);
        foreach (Track t in pool)
        {
            keyed.Add((t, rng.Next()));
        }
        keyed.Sort(static (a, b) =>
        {
            int byUsage = a.Track.TimesUsed.CompareTo(b.Track.TimesUsed);
            return byUsage != 0 ? byUsage : a.Key.CompareTo(b.Key);
        });

        long targetMs = Math.Max(1, settings.TargetDurationMinutes) * 60_000L;
        int defaultCrossfadeMs = Math.Max(0, settings.CrossfadeMs);

        List<MixItem> items = new(keyed.Count);
        long accumulator = 0;
        long? prevEffectiveMs = null;

        foreach ((Track track, _) in keyed)
        {
            long thisEff = Math.Max(0, track.EffectiveDurationMs);

            int xfadeIn = 0;
            if (prevEffectiveMs is long prev)
            {
                long shorter = Math.Min(prev, thisEff);
                xfadeIn = (int)Math.Min(defaultCrossfadeMs, shorter / 2);
            }

            if (items.Count > 0)
            {
                items[^1].XfadeOutMs = xfadeIn;
            }

            items.Add(new MixItem
            {
                TrackId = track.Id,
                OrderIndex = items.Count,
                TrimmedMs = (int)Math.Min(int.MaxValue, thisEff),
                XfadeInMs = xfadeIn,
                XfadeOutMs = 0,
            });

            accumulator += thisEff - xfadeIn;
            prevEffectiveMs = thisEff;

            if (accumulator >= targetMs) break;
        }

        Mix mix = new()
        {
            ProjectId = projectId,
            IndexInProject = indexInProject,
            TargetMin = Math.Max(0, settings.TargetDurationMinutes),
            ActualSec = (int)Math.Min(int.MaxValue, accumulator / 1000),
            Mode = MixMode.Reuse,
            OutputFormat = settings.OutputFormat,
            OutputPath = null,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = MixStatus.Planned,
        };

        return new PlannedMix(mix, items, sourceBatchIds);
    }

    private static HashSet<long> ToTrackSet(PlannedMix mix)
    {
        HashSet<long> set = new(mix.Items.Count);
        foreach (MixItem item in mix.Items)
        {
            set.Add(item.TrackId);
        }
        return set;
    }

    private static double ComputeMaxOverlap(
        HashSet<long> candidate,
        IReadOnlyList<HashSet<long>> priors)
    {
        if (candidate.Count == 0 || priors.Count == 0)
        {
            return 0;
        }

        double max = 0;
        foreach (HashSet<long> prior in priors)
        {
            if (prior.Count == 0) continue;

            int intersect = 0;
            // Iterate the smaller of the two for speed.
            HashSet<long> small = candidate.Count <= prior.Count ? candidate : prior;
            HashSet<long> large = ReferenceEquals(small, candidate) ? prior : candidate;
            foreach (long id in small)
            {
                if (large.Contains(id)) intersect++;
            }

            double overlap = (double)intersect / candidate.Count;
            if (overlap > max) max = overlap;
        }

        return max;
    }

    /// <summary>
    /// Stable RNG seed combining the project id, the mix's index in
    /// the project, and the current overlap-avoidance attempt number.
    /// Stays reproducible across process restarts.
    /// </summary>
    private static int ComputeSeed(long projectId, int indexInProject, int attempt)
    {
        unchecked
        {
            long combined = (projectId * 2654435769L)
                          ^ (indexInProject * 16777619L)
                          ^ (attempt * 524287L);
            return (int)(combined & 0xFFFFFFFFL);
        }
    }
}
