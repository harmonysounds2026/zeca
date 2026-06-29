using LAMG.Application.Abstractions.Persistence;
using LAMG.Application.Abstractions.Planning;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.UseCases.PlanMixes;

/// <summary>
/// Dispatches to the correct planner for the requested
/// <see cref="LAMG.Domain.Enums.MixMode"/> and persists each planned
/// mix (status <c>Planned</c>) before the renderer touches anything.
/// Both modes are idempotent: re-running after a crash will skip
/// batches that already have a Unique mix and only plan the missing
/// reuse mixes.
/// </summary>
public sealed class PlanMixesUseCase
{
    private readonly IUniqueModePlanner _uniquePlanner;
    private readonly IReuseModePlanner _reusePlanner;
    private readonly IBatchRepository _batchRepository;
    private readonly ITrackRepository _trackRepository;
    private readonly IMixRepository _mixRepository;
    private readonly ILogger<PlanMixesUseCase> _logger;

    public PlanMixesUseCase(
        IUniqueModePlanner uniquePlanner,
        IReuseModePlanner reusePlanner,
        IBatchRepository batchRepository,
        ITrackRepository trackRepository,
        IMixRepository mixRepository,
        ILogger<PlanMixesUseCase> logger)
    {
        _uniquePlanner = Guard.NotNull(uniquePlanner);
        _reusePlanner = Guard.NotNull(reusePlanner);
        _batchRepository = Guard.NotNull(batchRepository);
        _trackRepository = Guard.NotNull(trackRepository);
        _mixRepository = Guard.NotNull(mixRepository);
        _logger = Guard.NotNull(logger);
    }

    /// <summary>Returns the ids of the persisted, planned mixes.</summary>
    public Task<IReadOnlyList<long>> ExecuteAsync(
        PlanMixesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guard.Positive(request.ProjectId);
        ArgumentNullException.ThrowIfNull(request.Settings);

        return request.Mode switch
        {
            MixMode.Unique => PlanUniqueAsync(request, cancellationToken),
            MixMode.Reuse => PlanReuseAsync(request, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Mode,
                "Unknown mix mode."),
        };
    }

    // ---------------------------------------------------------------
    // Unique mode
    // ---------------------------------------------------------------

    private async Task<IReadOnlyList<long>> PlanUniqueAsync(
        PlanMixesRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Batch> batches = await _batchRepository
            .GetByProjectAsync(request.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (batches.Count == 0)
        {
            _logger.LogInformation(
                "Unique planning: project {ProjectId} has no batches.",
                request.ProjectId);
            return Array.Empty<long>();
        }

        HashSet<long> batchesWithUnique = await CollectBatchesWithUniqueMixesAsync(
            request.ProjectId, cancellationToken).ConfigureAwait(false);

        int currentIndex = await _mixRepository
            .GetMaxIndexAsync(request.ProjectId, cancellationToken)
            .ConfigureAwait(false) + 1;

        List<long> created = new(batches.Count);

        foreach (Batch batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (batchesWithUnique.Contains(batch.Id))
            {
                _logger.LogDebug(
                    "Batch {BatchId} already has a Unique mix; skipping.",
                    batch.Id);
                continue;
            }

            IReadOnlyList<Track> tracks = await _trackRepository
                .GetReadyByBatchAsync(batch.Id, cancellationToken)
                .ConfigureAwait(false);

            if (tracks.Count == 0)
            {
                _logger.LogInformation(
                    "Batch {BatchId} has no Ready tracks; no Unique mix produced.",
                    batch.Id);
                continue;
            }

            PlannedMix planned = _uniquePlanner.Plan(
                batch, tracks, request.Settings, currentIndex);

            if (planned.Items.Count == 0)
            {
                _logger.LogInformation(
                    "Unique planner produced an empty plan for batch {BatchId}; skipping.",
                    batch.Id);
                continue;
            }

            long mixId = await _mixRepository
                .AddPlannedAsync(
                    planned.Mix,
                    planned.Items,
                    planned.SourceBatchIds,
                    cancellationToken)
                .ConfigureAwait(false);

            created.Add(mixId);
            currentIndex++;
        }

        _logger.LogInformation(
            "Unique planning complete for project {ProjectId}: created {Count} mix(es).",
            request.ProjectId, created.Count);

        return created;
    }

    private async Task<HashSet<long>> CollectBatchesWithUniqueMixesAsync(
        long projectId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Mix> mixes = await _mixRepository
            .GetByProjectAsync(projectId, cancellationToken)
            .ConfigureAwait(false);

        HashSet<long> result = new();
        foreach (Mix m in mixes)
        {
            if (m.Mode != MixMode.Unique) continue;

            IReadOnlyList<long> sources = await _mixRepository
                .GetSourceBatchIdsAsync(m.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (long bid in sources)
            {
                result.Add(bid);
            }
        }

        return result;
    }

    // ---------------------------------------------------------------
    // Reuse mode
    // ---------------------------------------------------------------

    private async Task<IReadOnlyList<long>> PlanReuseAsync(
        PlanMixesRequest request,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<long>? poolBatches = request.ReusePoolBatchIds;
        if (poolBatches is null || poolBatches.Count == 0)
        {
            _logger.LogInformation(
                "Reuse planning: empty reuse pool; no mixes generated.");
            return Array.Empty<long>();
        }

        int requested = Math.Max(0, request.Settings.ReuseMixCount);
        if (requested == 0)
        {
            return Array.Empty<long>();
        }

        IReadOnlyList<Mix> existingMixes = await _mixRepository
            .GetByProjectAsync(request.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        int existingReuseCount = existingMixes.Count(m => m.Mode == MixMode.Reuse);
        int toGenerate = requested - existingReuseCount;
        if (toGenerate <= 0)
        {
            _logger.LogInformation(
                "Reuse planning: already have {Existing} of {Requested} reuse mixes; nothing to do.",
                existingReuseCount, requested);
            return Array.Empty<long>();
        }

        IReadOnlyList<Track> pool = await _trackRepository
            .GetReadyByBatchesAsync(poolBatches, cancellationToken)
            .ConfigureAwait(false);

        if (pool.Count == 0)
        {
            _logger.LogWarning(
                "Reuse planning: no Ready tracks in selected batches {Batches}; nothing to plan.",
                poolBatches);
            return Array.Empty<long>();
        }

        // Track-id sets of every existing mix (both Unique and Reuse)
        // so the planner can avoid producing near-duplicates of them.
        List<IReadOnlyCollection<long>> priorSets = new(existingMixes.Count);
        foreach (Mix m in existingMixes)
        {
            IReadOnlyList<MixItem> items = await _mixRepository
                .GetItemsAsync(m.Id, cancellationToken)
                .ConfigureAwait(false);

            HashSet<long> set = new(items.Count);
            foreach (MixItem mi in items)
            {
                set.Add(mi.TrackId);
            }
            priorSets.Add(set);
        }

        int firstIndex = await _mixRepository
            .GetMaxIndexAsync(request.ProjectId, cancellationToken)
            .ConfigureAwait(false) + 1;

        IReadOnlyList<PlannedMix> plans = _reusePlanner.Plan(
            poolTracks: pool,
            priorMixes: priorSets,
            reusePoolBatchIds: poolBatches,
            settings: request.Settings,
            firstIndexInProject: firstIndex,
            mixesToGenerate: toGenerate,
            projectId: request.ProjectId);

        List<long> created = new(plans.Count);
        foreach (PlannedMix planned in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (planned.Items.Count == 0) continue;

            long mixId = await _mixRepository
                .AddPlannedAsync(
                    planned.Mix,
                    planned.Items,
                    planned.SourceBatchIds,
                    cancellationToken)
                .ConfigureAwait(false);

            created.Add(mixId);
        }

        _logger.LogInformation(
            "Reuse planning complete for project {ProjectId}: created {Count} new reuse mix(es) (total now {Total}/{Requested}).",
            request.ProjectId, created.Count, existingReuseCount + created.Count, requested);

        return created;
    }
}
