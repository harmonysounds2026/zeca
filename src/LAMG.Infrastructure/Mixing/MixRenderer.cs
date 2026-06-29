using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.Persistence;
using LAMG.Application.Abstractions.System;
using LAMG.Application.Settings;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;
using LAMG.Infrastructure.FFmpeg;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Mixing;

/// <inheritdoc cref="IMixRenderer"/>
/// <remarks>
/// One mix at a time. Per call:
/// <list type="number">
///   <item>Pre-flight: load mix + items + tracks, locate ffmpeg,
///         filter out items whose source file is missing.</item>
///   <item>Resolve a collision-free output path
///         (<c>YYYY-MM-DD_mix_NNN.ext</c>, suffix bumped if taken).</item>
///   <item>Mark the mix <see cref="MixStatus.Rendering"/>.</item>
///   <item>Invoke ffmpeg writing to <c>output.tmp</c>.</item>
///   <item>Atomically rename to final path; mark
///         <see cref="MixStatus.Completed"/>; increment track usage.</item>
///   <item>On any failure: delete the .tmp, mark
///         <see cref="MixStatus.Failed"/>, return <see cref="Result"/> failure.</item>
/// </list>
/// </remarks>
public sealed class MixRenderer : IMixRenderer
{
    /// <summary>Maximum filename suffix to try when avoiding collisions.</summary>
    private const int MaxSuffixSearch = 999;

    private readonly FFmpegRunner _ffmpegRunner;
    private readonly FilterGraphBuilder _filterGraphBuilder;
    private readonly IFFmpegLocator _locator;
    private readonly IMixRepository _mixRepository;
    private readonly ITrackRepository _trackRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IFileSystemService _fileSystem;
    private readonly ISettingsService _settings;
    private readonly ILogger<MixRenderer> _logger;

    public MixRenderer(
        FFmpegRunner ffmpegRunner,
        FilterGraphBuilder filterGraphBuilder,
        IFFmpegLocator locator,
        IMixRepository mixRepository,
        ITrackRepository trackRepository,
        IProjectRepository projectRepository,
        IFileSystemService fileSystem,
        ISettingsService settings,
        ILogger<MixRenderer> logger)
    {
        _ffmpegRunner = Guard.NotNull(ffmpegRunner);
        _filterGraphBuilder = Guard.NotNull(filterGraphBuilder);
        _locator = Guard.NotNull(locator);
        _mixRepository = Guard.NotNull(mixRepository);
        _trackRepository = Guard.NotNull(trackRepository);
        _projectRepository = Guard.NotNull(projectRepository);
        _fileSystem = Guard.NotNull(fileSystem);
        _settings = Guard.NotNull(settings);
        _logger = Guard.NotNull(logger);
    }

    public async Task<Result> RenderAsync(
        long mixId,
        IProgress<RenderProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        Guard.Positive(mixId);

        ReportProgress(progress, "Validating", 0, 0, 0.0);

        // ---- Load mix ----
        Mix? mix = await _mixRepository.GetByIdAsync(mixId, cancellationToken).ConfigureAwait(false);
        if (mix is null)
        {
            return Result.Failure($"Mix {mixId} not found.");
        }

        // Idempotent: completed mixes need no work.
        if (mix.Status == MixStatus.Completed
            && !string.IsNullOrEmpty(mix.OutputPath)
            && File.Exists(mix.OutputPath))
        {
            _logger.LogDebug("Mix {Id} already completed; skipping render.", mixId);
            ReportProgress(progress, "AlreadyCompleted", 0, 0, 1.0);
            return Result.Success();
        }

        // ---- Load items + tracks ----
        IReadOnlyList<MixItem> rawItems = await _mixRepository
            .GetItemsAsync(mixId, cancellationToken)
            .ConfigureAwait(false);

        if (rawItems.Count == 0)
        {
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure($"Mix {mixId} has no items to render.");
        }

        Dictionary<long, Track> tracksById = new(rawItems.Count);
        foreach (long trackId in rawItems.Select(static i => i.TrackId).Distinct())
        {
            Track? t = await _trackRepository
                .GetByIdAsync(trackId, cancellationToken)
                .ConfigureAwait(false);
            if (t is not null)
            {
                tracksById[trackId] = t;
            }
        }

        // Skip-on-missing: filter out items whose source file is gone,
        // recompute crossfades for the remaining chain.
        List<MixItem> renderItems = FilterAndRecomputeChain(
            rawItems, tracksById, _settings.Current);

        if (renderItems.Count == 0)
        {
            _logger.LogError("Mix {Id}: every source track is missing or invalid.", mixId);
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure("No usable source tracks for this mix.");
        }

        // ---- Locate ffmpeg ----
        string? ffmpegPath = _locator.GetFFmpegPath();
        if (ffmpegPath is null)
        {
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure("ffmpeg executable could not be located.");
        }

        // ---- Project (for output folder) ----
        Project? project = await _projectRepository
            .GetByIdAsync(mix.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (project is null || string.IsNullOrWhiteSpace(project.OutputFolder))
        {
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure($"Project {mix.ProjectId} has no output folder configured.");
        }

        _fileSystem.EnsureDirectory(project.OutputFolder);

        // ---- Resolve output path + free-space check ----
        AppSettings settings = _settings.Current;
        string extension = settings.OutputFormat == OutputFormat.Mp3 ? "mp3" : "wav";
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        string outputPath = ResolveAvailableOutputPath(
            project.OutputFolder, mix.IndexInProject, extension, today);
        string tempPath = outputPath + ".tmp";

        long needed = EstimateOutputSizeBytes(mix, renderItems, settings);
        long available = _fileSystem.GetFreeSpaceBytes(project.OutputFolder);
        if (available < needed * 2)
        {
            _logger.LogError(
                "Mix {Id}: insufficient disk space. Need ~{NeedMb} MB plus headroom, have {HaveMb} MB.",
                mixId, needed / 1_048_576, available / 1_048_576);
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure("Insufficient disk space for this mix.");
        }

        // ---- Build args ----
        IReadOnlyList<string> args;
        try
        {
            args = _filterGraphBuilder.Build(renderItems, tracksById, settings, tempPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mix {Id}: failed to build filter graph.", mixId);
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure($"Failed to build filter graph: {ex.Message}", ex);
        }

        // ---- Mark Rendering + invoke ffmpeg ----
        await _mixRepository
            .UpdateStatusAsync(mixId, MixStatus.Rendering, cancellationToken)
            .ConfigureAwait(false);

        TryDelete(tempPath);
        ReportProgress(progress, "Encoding", 0, renderItems.Count, 0.10);

        FFmpegResult ffmpegResult;
        try
        {
            ffmpegResult = await _ffmpegRunner.RunAsync(
                ffmpegPath,
                args,
                settings.CpuMode,
                stderrLine: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            // The orchestrator's OCE handler will reset state.
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            _logger.LogError(ex, "Mix {Id}: ffmpeg invocation threw.", mixId);
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure($"ffmpeg threw: {ex.Message}", ex);
        }

        if (!ffmpegResult.IsSuccess
            || !File.Exists(tempPath)
            || new FileInfo(tempPath).Length == 0)
        {
            string detail = ffmpegResult.StandardError.Trim();
            if (detail.Length > 800) detail = detail[..800] + "…";
            _logger.LogError(
                "Mix {Id}: ffmpeg failed (exit {Code}). Stderr: {Stderr}",
                mixId, ffmpegResult.ExitCode, detail);
            TryDelete(tempPath);
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure($"ffmpeg exit {ffmpegResult.ExitCode}: {detail}");
        }

        // ---- Atomic rename ----
        ReportProgress(progress, "Finalizing", renderItems.Count, renderItems.Count, 0.95);
        try
        {
            await _fileSystem
                .AtomicReplaceAsync(tempPath, outputPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mix {Id}: failed to finalize output.", mixId);
            TryDelete(tempPath);
            await MarkFailedAsync(mixId, cancellationToken).ConfigureAwait(false);
            return Result.Failure($"Failed to finalize output: {ex.Message}", ex);
        }

        // ---- DB updates ----
        int actualSec = ComputeActualSeconds(renderItems);
        await _mixRepository
            .MarkCompletedAsync(mixId, outputPath, actualSec, cancellationToken)
            .ConfigureAwait(false);

        long[] usedTrackIds = renderItems
            .Select(static i => i.TrackId)
            .Distinct()
            .ToArray();
        await _trackRepository
            .IncrementUsageAsync(usedTrackIds, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Mix {Id} rendered: '{Path}' ({Sec}s, {Tracks} tracks).",
            mixId, outputPath, actualSec, renderItems.Count);

        ReportProgress(progress, "Done", renderItems.Count, renderItems.Count, 1.0);
        return Result.Success();
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Drops items whose source file is missing and re-derives the
    /// crossfade chain for the survivors. Returns deep clones so
    /// nothing in the DB-loaded objects is mutated.
    /// </summary>
    private List<MixItem> FilterAndRecomputeChain(
        IReadOnlyList<MixItem> source,
        Dictionary<long, Track> tracksById,
        AppSettings settings)
    {
        List<MixItem> kept = new(source.Count);
        foreach (MixItem item in source)
        {
            if (!tracksById.TryGetValue(item.TrackId, out Track? track))
            {
                _logger.LogWarning(
                    "Mix item {ItemId}: track {TrackId} record missing; skipping.",
                    item.Id, item.TrackId);
                continue;
            }

            if (!File.Exists(track.FullPath))
            {
                _logger.LogWarning(
                    "Mix item {ItemId}: source file '{Path}' missing; skipping.",
                    item.Id, track.FullPath);
                continue;
            }

            // Clone so we can mutate without touching the DB-loaded record.
            kept.Add(new MixItem
            {
                Id = item.Id,
                MixId = item.MixId,
                TrackId = item.TrackId,
                OrderIndex = item.OrderIndex,
                TrimmedMs = item.TrimmedMs,
                XfadeInMs = item.XfadeInMs,
                XfadeOutMs = item.XfadeOutMs,
            });
        }

        // Re-derive crossfades for the surviving chain.
        int crossfadeMs = Math.Max(0, settings.CrossfadeMs);
        for (int i = 0; i < kept.Count; i++)
        {
            MixItem item = kept[i];
            Track track = tracksById[item.TrackId];
            long thisEff = Math.Max(0, track.EffectiveDurationMs);
            item.OrderIndex = i;
            item.TrimmedMs = (int)Math.Min(int.MaxValue, thisEff);

            if (i == 0)
            {
                item.XfadeInMs = 0;
            }
            else
            {
                Track prev = tracksById[kept[i - 1].TrackId];
                long shorter = Math.Min(
                    Math.Max(0, prev.EffectiveDurationMs),
                    thisEff);
                item.XfadeInMs = (int)Math.Min(crossfadeMs, shorter / 2);
            }
        }

        // Stitch XfadeOut from the next item's XfadeIn.
        for (int i = 0; i < kept.Count - 1; i++)
        {
            kept[i].XfadeOutMs = kept[i + 1].XfadeInMs;
        }

        if (kept.Count > 0)
        {
            kept[^1].XfadeOutMs = 0;
        }

        return kept;
    }

    private string ResolveAvailableOutputPath(
        string folder,
        int indexInProject,
        string extension,
        DateOnly date)
    {
        // Try the natural index first.
        string candidate = _fileSystem.BuildOutputPath(folder, indexInProject, extension, date);
        if (!File.Exists(candidate) && !File.Exists(candidate + ".tmp"))
        {
            return candidate;
        }

        // Otherwise walk forward until we find a free suffix.
        for (int suffix = indexInProject + 1; suffix <= MaxSuffixSearch; suffix++)
        {
            candidate = _fileSystem.BuildOutputPath(folder, suffix, extension, date);
            if (!File.Exists(candidate) && !File.Exists(candidate + ".tmp"))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Could not find a free output filename in '{folder}' up to suffix {MaxSuffixSearch:D3}.");
    }

    private static long EstimateOutputSizeBytes(
        Mix mix,
        IReadOnlyList<MixItem> items,
        AppSettings settings)
    {
        int durationSec = ComputeActualSeconds(items);
        if (durationSec <= 0)
        {
            durationSec = Math.Max(60, mix.TargetMin * 60);
        }

        return settings.OutputFormat switch
        {
            OutputFormat.Mp3 => (long)Math.Max(64, settings.Mp3BitrateKbps) * 1000L / 8L * durationSec,
            OutputFormat.Wav => settings.WavBitDepth == 24
                ? 6L * 44100L * durationSec
                : 4L * 44100L * durationSec,
            _ => 200L * 1024L * 1024L,
        };
    }

    private static int ComputeActualSeconds(IReadOnlyList<MixItem> items)
    {
        long totalMs = 0;
        foreach (MixItem item in items)
        {
            // Per the planner: total = Σ effective_i − Σ xfade_j.
            // XfadeInMs of the first item is 0 by construction.
            totalMs += Math.Max(0, item.TrimmedMs) - Math.Max(0, item.XfadeInMs);
        }

        if (totalMs <= 0) return 0;
        if (totalMs > int.MaxValue * 1000L) return int.MaxValue;
        return (int)(totalMs / 1000L);
    }

    private async Task MarkFailedAsync(long mixId, CancellationToken cancellationToken)
    {
        try
        {
            await _mixRepository
                .UpdateStatusAsync(mixId, MixStatus.Failed, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A secondary failure here must not crash the pipeline.
            _logger.LogError(ex, "Failed to mark mix {Id} as Failed.", mixId);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Could not delete '{Path}'; will leave for the crash-recovery sweep.",
                path);
        }
    }

    private static void ReportProgress(
        IProgress<RenderProgress>? progress,
        string stage,
        int currentTrack,
        int totalTracks,
        double fraction)
    {
        if (progress is null) return;
        try
        {
            progress.Report(new RenderProgress(stage, currentTrack, totalTracks, fraction));
        }
        catch (Exception)
        {
            // Progress subscribers must never break the render.
        }
    }
}
