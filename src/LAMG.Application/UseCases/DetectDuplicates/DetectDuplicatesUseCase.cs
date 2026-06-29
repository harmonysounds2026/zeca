using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.UseCases.DetectDuplicates;

/// <summary>
/// Runs duplicate detection across freshly imported batches and applies
/// the user's chosen <see cref="DuplicateResolution"/> strategy.
/// </summary>
/// <remarks>
/// Resolution uses a <em>winners-minus-participants</em> set difference
/// so a track that wins one group but loses another is still kept. This
/// avoids over-skipping when a single track shows up in multiple
/// overlapping duplicate kinds (e.g. same file_hash AND same audio_hash).
/// </remarks>
public sealed class DetectDuplicatesUseCase
{
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly ITrackRepository _trackRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DetectDuplicatesUseCase> _logger;

    public DetectDuplicatesUseCase(
        IDuplicateDetector duplicateDetector,
        ITrackRepository trackRepository,
        IUnitOfWork unitOfWork,
        ILogger<DetectDuplicatesUseCase> logger)
    {
        _duplicateDetector = Guard.NotNull(duplicateDetector);
        _trackRepository = Guard.NotNull(trackRepository);
        _unitOfWork = Guard.NotNull(unitOfWork);
        _logger = Guard.NotNull(logger);
    }

    public async Task<DuplicateDetectionReport> DetectAsync(
        IReadOnlyCollection<long> batchIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batchIds);

        if (batchIds.Count == 0)
        {
            return new DuplicateDetectionReport(Array.Empty<DuplicateGroup>());
        }

        // Detection considers only Ready tracks. Pending tracks haven't
        // produced hashes yet, and Corrupted aren't usable.
        IReadOnlyList<Track> candidates = await _trackRepository
            .GetReadyByBatchesAsync(batchIds, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<DuplicateGroup> groups = await _duplicateDetector
            .FindAsync(candidates, existing: Array.Empty<Track>(), cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Duplicate detection: scanned {Tracks} tracks, found {Groups} groups across batches {Batches}.",
            candidates.Count, groups.Count, batchIds);

        return new DuplicateDetectionReport(groups);
    }

    public async Task ApplyResolutionAsync(
        DuplicateDetectionReport report,
        DuplicateResolution resolution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.HasDuplicates)
        {
            return;
        }

        switch (resolution)
        {
            case DuplicateResolution.ImportAll:
                _logger.LogInformation(
                    "Duplicates: ImportAll - keeping every member of {Groups} groups Ready.",
                    report.Groups.Count);
                return;

            case DuplicateResolution.SkipDuplicates:
            case DuplicateResolution.ReplaceExisting:
                await SkipNonWinnersAsync(report, resolution, cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(resolution),
                    resolution,
                    "Unknown duplicate resolution.");
        }
    }

    private async Task SkipNonWinnersAsync(
        DuplicateDetectionReport report,
        DuplicateResolution resolution,
        CancellationToken cancellationToken)
    {
        // For SkipDuplicates the lowest-id wins (oldest entry kept);
        // for ReplaceExisting the highest-id wins (newest entry kept).
        // Cross-group correctness: a track is kept if ANY group chose it.
        HashSet<long> winners = new();
        HashSet<long> participants = new();

        foreach (DuplicateGroup group in report.Groups)
        {
            if (group.TrackIds.Count == 0) continue;

            long winner = resolution == DuplicateResolution.ReplaceExisting
                ? Max(group.TrackIds)
                : Min(group.TrackIds);

            winners.Add(winner);
            foreach (long id in group.TrackIds)
            {
                participants.Add(id);
            }
        }

        HashSet<long> toSkip = new(participants);
        toSkip.ExceptWith(winners);

        if (toSkip.Count == 0)
        {
            _logger.LogInformation(
                "Duplicates: {Resolution} - every duplicate is also a winner; no rows skipped.",
                resolution);
            return;
        }

        // Bulk-update inside a single transaction so resolution is
        // atomic from the caller's perspective. The session passed to
        // each UpdateStatusAsync call routes the UPDATEs onto the
        // UnitOfWork's connection + transaction, so a mid-loop failure
        // rolls every prior row back.
        await _unitOfWork.ExecuteAsync(async (session, ct) =>
        {
            foreach (long id in toSkip)
            {
                await _trackRepository
                    .UpdateStatusAsync(id, TrackStatus.Skipped, ct, session)
                    .ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Duplicates: {Resolution} - marked {Skipped} tracks Skipped (kept {Winners} winners).",
            resolution, toSkip.Count, winners.Count);
    }

    private static long Min(IReadOnlyList<long> ids)
    {
        long min = ids[0];
        for (int i = 1; i < ids.Count; i++)
        {
            if (ids[i] < min) min = ids[i];
        }
        return min;
    }

    private static long Max(IReadOnlyList<long> ids)
    {
        long max = ids[0];
        for (int i = 1; i < ids.Count; i++)
        {
            if (ids[i] > max) max = ids[i];
        }
        return max;
    }
}
