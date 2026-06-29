using System.Globalization;
using System.Text.RegularExpressions;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Infrastructure.FFmpeg;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="ISilenceDetector"/>
/// <remarks>
/// Runs ffmpeg with the <c>silencedetect</c> filter and parses the
/// resulting stderr log. Only the leading and trailing silence
/// segments are tracked; mid-file silences are ignored for v1.
/// </remarks>
public sealed partial class SilenceDetector : ISilenceDetector
{
    /// <summary>Leading silence is the first segment that begins within this margin of t=0.</summary>
    private const double LeadingToleranceSec = 0.10;

    /// <summary>Trailing silence is the last segment that ends within this margin of file end.</summary>
    private const double TrailingToleranceSec = 0.10;

    [GeneratedRegex(@"silence_start:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex SilenceStartRegex();

    [GeneratedRegex(@"silence_duration:\s*(-?\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex SilenceDurationRegex();

    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled)]
    private static partial Regex DurationRegex();

    private readonly FFmpegRunner _ffmpegRunner;
    private readonly IFFmpegLocator _locator;
    private readonly ISettingsService _settings;
    private readonly ILogger<SilenceDetector> _logger;

    public SilenceDetector(
        FFmpegRunner ffmpegRunner,
        IFFmpegLocator locator,
        ISettingsService settings,
        ILogger<SilenceDetector> logger)
    {
        _ffmpegRunner = Guard.NotNull(ffmpegRunner);
        _locator = Guard.NotNull(locator);
        _settings = Guard.NotNull(settings);
        _logger = Guard.NotNull(logger);
    }

    public async Task<SilenceMeasurement> DetectAsync(
        string filePath,
        double noiseFloorDb,
        int minSilenceMs,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(filePath);
        Guard.NotNegative(minSilenceMs);

        string? ffmpegPath = _locator.GetFFmpegPath();
        if (ffmpegPath is null)
        {
            throw new InvalidOperationException("ffmpeg executable could not be located.");
        }

        // silencedetect filter: noise= in dB, d= in seconds (float).
        string filter = string.Create(
            CultureInfo.InvariantCulture,
            $"silencedetect=noise={noiseFloorDb:0.##}dB:d={minSilenceMs / 1000.0:0.###}");

        IReadOnlyList<string> args =
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel", "info",
            "-i", filePath,
            "-vn",
            "-af", filter,
            "-f", "null",
            "-",
        ];

        FFmpegResult result = await _ffmpegRunner.RunAsync(
            ffmpegPath,
            args,
            _settings.Current.CpuMode,
            stderrLine: null,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            // silencedetect failures are not fatal at the use-case level,
            // but we propagate the error so the composite analyzer can
            // soft-fail with a clear log.
            throw new InvalidOperationException(
                $"silencedetect failed for '{filePath}' (exit {result.ExitCode}): " +
                $"{result.StandardError.Trim()}");
        }

        return Parse(result.StandardError);
    }

    /// <summary>
    /// Parses the stderr text into a <see cref="SilenceMeasurement"/>.
    /// Internal for testability.
    /// </summary>
    internal static SilenceMeasurement Parse(string stderr)
    {
        double durationSec = ParseDuration(stderr);
        var segments = ParseSegments(stderr);

        int leadingMs = 0;
        int trailingMs = 0;

        if (segments.Count > 0)
        {
            // Leading: first segment that starts at (or before) the tolerance.
            (double start, double duration) first = segments[0];
            if (first.start <= LeadingToleranceSec && first.duration > 0)
            {
                leadingMs = ToMs(first.duration);
            }

            // Trailing: last segment whose end is within tolerance of the
            // file's reported duration. If duration was not parseable we
            // skip trailing detection (avoiding false positives).
            if (durationSec > 0)
            {
                (double start, double duration) last = segments[^1];
                double end = last.start + last.duration;
                if (end >= durationSec - TrailingToleranceSec && last.duration > 0)
                {
                    trailingMs = ToMs(last.duration);
                }
            }
        }

        return new SilenceMeasurement(leadingMs, trailingMs);
    }

    private static double ParseDuration(string stderr)
    {
        Match m = DurationRegex().Match(stderr);
        if (!m.Success)
        {
            return 0;
        }

        if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hh)
            || !int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mm)
            || !double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ss))
        {
            return 0;
        }

        return (hh * 3600d) + (mm * 60d) + ss;
    }

    private static List<(double start, double duration)> ParseSegments(string stderr)
    {
        MatchCollection startMatches = SilenceStartRegex().Matches(stderr);
        MatchCollection durationMatches = SilenceDurationRegex().Matches(stderr);

        var result = new List<(double start, double duration)>(startMatches.Count);

        for (int i = 0; i < startMatches.Count; i++)
        {
            if (!double.TryParse(
                startMatches[i].Groups[1].Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double start))
            {
                continue;
            }

            double duration = 0;
            if (i < durationMatches.Count
                && double.TryParse(
                    durationMatches[i].Groups[1].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double parsed))
            {
                duration = parsed;
            }

            result.Add((start, duration));
        }

        return result;
    }

    private static int ToMs(double seconds)
    {
        if (seconds <= 0) return 0;
        double ms = seconds * 1000;
        if (ms > int.MaxValue) return int.MaxValue;
        return (int)Math.Round(ms);
    }
}
