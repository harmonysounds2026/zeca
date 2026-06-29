using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Application.UseCases.AnalyzeTracks;

/// <summary>
/// Runs the full per-track analysis pipeline against every Pending
/// track in the supplied batches. One bad file never stops the job:
/// each iteration has its own try/catch, and decoder failures merely
/// flip the track's status to <see cref="TrackStatus.Corrupted"/>.
/// </summary>
public sealed class AnalyzeTracksUseCase
{
    private readonly IAudioAnalysisService _analysisService;
    private readonly ITrackRepository _trackRepository;
    private readonly ILogger<AnalyzeTracksUseCase> _logger;

    public AnalyzeTracksUseCase(
        IAudioAnalysisService analysisService,
        ITrackRepository trackRepository,
        ILogger<AnalyzeTracksUseCase> logger)
    {
        _analysisService = Guard.NotNull(analysisService);
        _trackRepository = Guard.NotNull(trackRepository);
        _logger = Guard.NotNull(logger);
    }

    /// <summary>Returns the number of tracks that ended in <c>Ready</c> status.</summary>
    public async Task<int> ExecuteAsync(
        AnalyzeTracksRequest request,
        IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BatchIds);
        Guard.Positive(request.MaxDegreeOfParallelism);

        if (request.BatchIds.Count == 0)
        {
            return 0;
        }

        IReadOnlyList<Track> allInBatches = await _trackRepository
            .GetByBatchesAsync(request.BatchIds, cancellationToken)
            .ConfigureAwait(false);

        // Only Pending tracks need work. Already-Ready tracks may be
        // present after a resumed crash; skip them silently.
        List<Track> pending = new(allInBatches.Count);
        foreach (Track t in allInBatches)
        {
            if (t.Status == TrackStatus.Pending)
            {
                pending.Add(t);
            }
        }

        if (pending.Count == 0)
        {
            _logger.LogDebug("No Pending tracks to analyze in batches {Batches}.", request.BatchIds);
            return CountStatus(allInBatches, TrackStatus.Ready);
        }

        int completed = 0;
        int readyCount = CountStatus(allInBatches, TrackStatus.Ready);
        int corruptedCount = 0;

        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = request.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(pending, options, async (track, innerCt) =>
        {
            try
            {
                Result<Track> analysis = await _analysisService
                    .AnalyzeAsync(track.FullPath, track.BatchId, innerCt)
                    .ConfigureAwait(false);

                if (analysis.IsSuccess && analysis.Value is { } populated)
                {
                    // Preserve the existing row id; AudioAnalysisService
                    // returns a fresh Track with id = 0.
                    populated.Id = track.Id;
                    populated.Status = TrackStatus.Ready;

                    await _trackRepository
                        .UpdateAsync(populated, innerCt)
                        .ConfigureAwait(false);

                    Interlocked.Increment(ref readyCount);
                }
                else
                {
                    await MarkCorruptedAsync(track, analysis.Error, innerCt).ConfigureAwait(false);
                    Interlocked.Increment(ref corruptedCount);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Analysis threw for track {Id} ('{Path}'); marking corrupted.",
                    track.Id, track.FullPath);

                await MarkCorruptedAsync(track, ex.Message, innerCt).ConfigureAwait(false);
                Interlocked.Increment(ref corruptedCount);
            }
            finally
            {
                int done = Interlocked.Increment(ref completed);
                try
                {
                    progress?.Report(done);
                }
                catch
                {
                    // Subscriber bugs must not interrupt the analysis.
                }
            }
        }).ConfigureAwait(false);

        _logger.LogInformation(
            "Analysis complete: {Ready} ready, {Corrupted} corrupted across batches {Batches}.",
            readyCount, corruptedCount, request.BatchIds);

        return readyCount;
    }

    private async Task MarkCorruptedAsync(Track track, string? reason, CancellationToken ct)
    {
        try
        {
            await _trackRepository
                .UpdateStatusAsync(track.Id, TrackStatus.Corrupted, ct)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "Track {Id} ('{Path}') marked Corrupted: {Reason}.",
                track.Id, track.FullPath, reason ?? "unknown");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Secondary failure (db down?) - log loudly but keep going.
            _logger.LogError(
                ex,
                "Failed to mark track {Id} Corrupted; job continues anyway.",
                track.Id);
        }
    }

    private static int CountStatus(IReadOnlyList<Track> tracks, TrackStatus status)
    {
        int n = 0;
        foreach (Track t in tracks)
        {
            if (t.Status == status) n++;
        }
        return n;
    }
}
