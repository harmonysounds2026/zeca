using System.Globalization;
using System.Text.RegularExpressions;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Infrastructure.FFmpeg;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="ILoudnessAnalyzer"/>
/// <remarks>
/// Runs ffmpeg with the <c>ebur128</c> filter and parses the final
/// "Summary:" block from stderr. ebur128 also emits intermediate
/// progress lines that match the same regex; the summary value is
/// always the last occurrence, so we take the last match.
/// </remarks>
public sealed partial class LoudnessAnalyzer : ILoudnessAnalyzer
{
    [GeneratedRegex(@"I:\s*(-?\d+(?:\.\d+)?)\s*LUFS", RegexOptions.Compiled)]
    private static partial Regex IntegratedRegex();

    [GeneratedRegex(@"Peak:\s*(-?\d+(?:\.\d+)?)\s*dBFS", RegexOptions.Compiled)]
    private static partial Regex TruePeakRegex();

    private readonly FFmpegRunner _ffmpegRunner;
    private readonly IFFmpegLocator _locator;
    private readonly ISettingsService _settings;
    private readonly ILogger<LoudnessAnalyzer> _logger;

    public LoudnessAnalyzer(
        FFmpegRunner ffmpegRunner,
        IFFmpegLocator locator,
        ISettingsService settings,
        ILogger<LoudnessAnalyzer> logger)
    {
        _ffmpegRunner = Guard.NotNull(ffmpegRunner);
        _locator = Guard.NotNull(locator);
        _settings = Guard.NotNull(settings);
        _logger = Guard.NotNull(logger);
    }

    public async Task<LoudnessMeasurement> MeasureAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(filePath);

        string? ffmpegPath = _locator.GetFFmpegPath();
        if (ffmpegPath is null)
        {
            throw new InvalidOperationException("ffmpeg executable could not be located.");
        }

        IReadOnlyList<string> args =
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel", "info",
            "-i", filePath,
            "-vn",
            "-af", "ebur128=peak=true",
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
            throw new InvalidOperationException(
                $"ebur128 failed for '{filePath}' (exit {result.ExitCode}): " +
                $"{result.StandardError.Trim()}");
        }

        return Parse(result.StandardError, filePath);
    }

    /// <summary>
    /// Parses the stderr text into a <see cref="LoudnessMeasurement"/>.
    /// Internal for testability.
    /// </summary>
    internal static LoudnessMeasurement Parse(string stderr, string filePath)
    {
        double? integratedLufs = LastDouble(IntegratedRegex(), stderr);
        double? truePeakDb = LastDouble(TruePeakRegex(), stderr);

        if (integratedLufs is null || truePeakDb is null)
        {
            throw new InvalidOperationException(
                $"ebur128 output could not be parsed for '{filePath}'. " +
                $"Integrated={integratedLufs?.ToString(CultureInfo.InvariantCulture) ?? "missing"}, " +
                $"Peak={truePeakDb?.ToString(CultureInfo.InvariantCulture) ?? "missing"}.");
        }

        return new LoudnessMeasurement(integratedLufs.Value, truePeakDb.Value);
    }

    private static double? LastDouble(Regex regex, string text)
    {
        MatchCollection matches = regex.Matches(text);
        if (matches.Count == 0)
        {
            return null;
        }

        string captured = matches[^1].Groups[1].Value;
        return double.TryParse(captured, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : null;
    }
}
