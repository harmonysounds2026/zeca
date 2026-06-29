using System.Globalization;
using System.Text;

using LAMG.Application.Abstractions.System;
using LAMG.Application.Settings;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.FFmpeg;

/// <summary>
/// Builds the complete <c>ffmpeg</c> argument list for rendering a
/// single mix. The filter chain per track is:
/// <para>
///   <c>[i:a] atrim → asetpts → aresample → loudnorm → aformat → [ai]</c>
/// </para>
/// Adjacent track chains are then stitched together with
/// <c>acrossfade=d=&lt;sec&gt;:c1=tri:c2=tri</c>, producing a single
/// <c>[out]</c> stream which is mapped to the output file.
/// </summary>
/// <remarks>
/// Loudnorm uses the single-pass variant (the design choice locked in
/// v1): conservative, fast, no second analysis pass. The target LUFS
/// and true-peak ceiling come from <see cref="AppSettings"/>; the LRA
/// (loudness range) is a constant 11 — a reasonable music default.
/// </remarks>
public sealed class FilterGraphBuilder
{
    private const int CanonicalSampleRate = 44100;
    private const int CanonicalChannels = 2;
    private const double LoudnessRangeLra = 11.0;
    private const double MinCrossfadeSec = 0.001;

    private readonly ICpuModeApplier _cpuModeApplier;
    private readonly ILogger<FilterGraphBuilder> _logger;

    public FilterGraphBuilder(
        ICpuModeApplier cpuModeApplier,
        ILogger<FilterGraphBuilder> logger)
    {
        _cpuModeApplier = Guard.NotNull(cpuModeApplier);
        _logger = Guard.NotNull(logger);
    }

    /// <summary>
    /// Builds the full ffmpeg argument list. The output file path goes
    /// last (ffmpeg's convention).
    /// </summary>
    public IReadOnlyList<string> Build(
        IReadOnlyList<MixItem> orderedItems,
        IReadOnlyDictionary<long, Track> tracksById,
        AppSettings settings,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(orderedItems);
        ArgumentNullException.ThrowIfNull(tracksById);
        ArgumentNullException.ThrowIfNull(settings);
        Guard.NotNullOrWhiteSpace(outputPath);

        if (orderedItems.Count == 0)
        {
            throw new ArgumentException(
                "Cannot build filter graph for an empty mix.",
                nameof(orderedItems));
        }

        List<string> args = new(capacity: 16 + (orderedItems.Count * 2));

        AddGlobalFlags(args);
        AddInputs(args, orderedItems, tracksById);

        args.Add("-filter_complex");
        args.Add(BuildFilterExpression(orderedItems, tracksById, settings));

        // Single-track mixes have no crossfade chain; map [a0] directly.
        string finalLabel = orderedItems.Count == 1 ? "a0" : "out";
        args.Add("-map");
        args.Add($"[{finalLabel}]");

        AddOutputCodec(args, settings);
        AddOutputFormat(args);
        AddOutputMuxer(args, settings);
        AddThreadCount(args, settings);

        args.Add(outputPath);

        _logger.LogDebug(
            "FilterGraph built: {Items} tracks, format {Format}, output '{Out}'.",
            orderedItems.Count, settings.OutputFormat, outputPath);

        return args;
    }

    private static void AddGlobalFlags(List<string> args)
    {
        args.Add("-nostdin");
        args.Add("-hide_banner");
        args.Add("-loglevel");
        args.Add("error");
        // -y because we own the .tmp file lifecycle externally.
        args.Add("-y");
    }

    private static void AddInputs(
        List<string> args,
        IReadOnlyList<MixItem> orderedItems,
        IReadOnlyDictionary<long, Track> tracksById)
    {
        foreach (MixItem item in orderedItems)
        {
            if (!tracksById.TryGetValue(item.TrackId, out Track? track))
            {
                throw new InvalidOperationException(
                    $"Track {item.TrackId} referenced by MixItem is missing from the dictionary.");
            }

            args.Add("-i");
            args.Add(track.FullPath);
        }
    }

    private static void AddOutputCodec(List<string> args, AppSettings settings)
    {
        if (settings.OutputFormat == OutputFormat.Mp3)
        {
            // libmp3lame CBR is the most predictable + widely compatible mp3 path.
            int bitrate = Math.Max(64, settings.Mp3BitrateKbps);
            args.Add("-c:a");
            args.Add("libmp3lame");
            args.Add("-b:a");
            args.Add(string.Create(CultureInfo.InvariantCulture, $"{bitrate}k"));
        }
        else
        {
            // pcm_s24le for 24-bit, pcm_s16le for any other depth.
            args.Add("-c:a");
            args.Add(settings.WavBitDepth == 24 ? "pcm_s24le" : "pcm_s16le");
        }
    }

    private static void AddOutputFormat(List<string> args)
    {
        // Match the canonical filter-graph output: 44.1 kHz stereo.
        args.Add("-ar");
        args.Add(CanonicalSampleRate.ToString(CultureInfo.InvariantCulture));
        args.Add("-ac");
        args.Add(CanonicalChannels.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Forces the output muxer (container) explicitly with <c>-f</c>.
    /// Without this, ffmpeg infers the format from the output file
    /// extension - but the renderer writes through a temporary
    /// <c>.tmp</c> file (atomic rename pattern), so the actual filename
    /// it sees is e.g. <c>...mp3.tmp</c> and the inference fails with
    /// "Unable to choose an output format". Setting <c>-f</c> bypasses
    /// the inference and matches the codec selected by
    /// <see cref="AddOutputCodec"/>.
    /// </summary>
    private static void AddOutputMuxer(List<string> args, AppSettings settings)
    {
        args.Add("-f");
        args.Add(settings.OutputFormat == OutputFormat.Mp3 ? "mp3" : "wav");
    }

    private void AddThreadCount(List<string> args, AppSettings settings)
    {
        int threads = _cpuModeApplier.GetFFmpegThreadCount(settings.CpuMode);
        if (threads <= 0)
        {
            // 0 means "let ffmpeg pick" — don't pass the flag.
            return;
        }

        args.Add("-threads");
        args.Add(threads.ToString(CultureInfo.InvariantCulture));
    }

    private static string BuildFilterExpression(
        IReadOnlyList<MixItem> orderedItems,
        IReadOnlyDictionary<long, Track> tracksById,
        AppSettings settings)
    {
        int n = orderedItems.Count;
        List<string> chains = new(capacity: n + Math.Max(0, n - 1));

        for (int i = 0; i < n; i++)
        {
            chains.Add(BuildTrackChain(i, orderedItems[i], tracksById, settings));
        }

        if (n >= 2)
        {
            string left = "a0";
            for (int i = 1; i < n; i++)
            {
                MixItem item = orderedItems[i];

                double xfadeSec = Math.Max(0, item.XfadeInMs) / 1000.0;
                if (xfadeSec < MinCrossfadeSec)
                {
                    // ffmpeg's acrossfade requires d > 0; clamp to a
                    // tiny but legal duration when the planner asked
                    // for zero (e.g. very short tracks at the edge).
                    xfadeSec = MinCrossfadeSec;
                }

                string right = $"a{i}";
                string output = (i == n - 1) ? "out" : $"m{i}";

                chains.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{left}][{right}]acrossfade=d={xfadeSec:F3}:c1=tri:c2=tri[{output}]"));

                left = output;
            }
        }

        return string.Join(";", chains);
    }

    private static string BuildTrackChain(
        int index,
        MixItem item,
        IReadOnlyDictionary<long, Track> tracksById,
        AppSettings settings)
    {
        Track track = tracksById[item.TrackId];

        // Compute trim window in seconds (atrim accepts float seconds).
        double startSec = Math.Max(0, track.SilenceLeadMs) / 1000.0;
        long usableMs = Math.Max(
            1,
            track.DurationMs - Math.Max(0, track.SilenceTailMs));
        double endSec = usableMs / 1000.0;
        if (endSec <= startSec)
        {
            // Degenerate measurement; keep at least 100 ms so ffmpeg
            // doesn't fail outright.
            endSec = startSec + 0.1;
        }

        string targetLufs = settings.NormalizationTargetLufs
            .ToString("F1", CultureInfo.InvariantCulture);
        string truePeakDb = settings.NormalizationTruePeakDb
            .ToString("F1", CultureInfo.InvariantCulture);
        string lra = LoudnessRangeLra.ToString("F1", CultureInfo.InvariantCulture);
        string sampleRate = CanonicalSampleRate.ToString(CultureInfo.InvariantCulture);

        StringBuilder sb = new(capacity: 256);
        sb.Append('[').Append(index).Append(":a]");
        sb.Append("atrim=start=").Append(startSec.ToString("F3", CultureInfo.InvariantCulture));
        sb.Append(":end=").Append(endSec.ToString("F3", CultureInfo.InvariantCulture));
        sb.Append(",asetpts=PTS-STARTPTS");
        sb.Append(",aresample=").Append(sampleRate);
        sb.Append(",loudnorm=I=").Append(targetLufs)
          .Append(":TP=").Append(truePeakDb)
          .Append(":LRA=").Append(lra);
        sb.Append(",aformat=channel_layouts=stereo:sample_rates=").Append(sampleRate);
        sb.Append("[a").Append(index).Append(']');
        return sb.ToString();
    }
}
