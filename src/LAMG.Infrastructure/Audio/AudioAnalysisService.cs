using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Settings;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Domain.Models;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="IAudioAnalysisService"/>
/// <remarks>
/// Composes the five per-track analysis steps. The first three (probe,
/// file hash, audio hash) are <em>hard</em> requirements: if any of
/// them fails, the whole analysis returns <see cref="Result{Track}"/>
/// failure and the use case marks the track <see cref="TrackStatus.Corrupted"/>.
/// The last two (silence, loudness) are <em>soft</em>: failures are
/// logged and replaced with defaults (zero silence, null loudness),
/// keeping the track usable for mix planning.
/// </remarks>
public sealed class AudioAnalysisService : IAudioAnalysisService
{
    private readonly IFFprobeService _probe;
    private readonly IFileHasher _fileHasher;
    private readonly IAudioHasher _audioHasher;
    private readonly ISilenceDetector _silenceDetector;
    private readonly ILoudnessAnalyzer _loudnessAnalyzer;
    private readonly ISettingsService _settings;
    private readonly ILogger<AudioAnalysisService> _logger;

    public AudioAnalysisService(
        IFFprobeService probe,
        IFileHasher fileHasher,
        IAudioHasher audioHasher,
        ISilenceDetector silenceDetector,
        ILoudnessAnalyzer loudnessAnalyzer,
        ISettingsService settings,
        ILogger<AudioAnalysisService> logger)
    {
        _probe = Guard.NotNull(probe);
        _fileHasher = Guard.NotNull(fileHasher);
        _audioHasher = Guard.NotNull(audioHasher);
        _silenceDetector = Guard.NotNull(silenceDetector);
        _loudnessAnalyzer = Guard.NotNull(loudnessAnalyzer);
        _settings = Guard.NotNull(settings);
        _logger = Guard.NotNull(logger);
    }

    public async Task<Result<Track>> AnalyzeAsync(
        string filePath,
        long batchId,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(filePath);
        Guard.Positive(batchId);

        AppSettings settings = _settings.Current;

        try
        {
            // ---- Hard fails: probe, file hash, audio hash ----
            ProbeResult probe = await _probe
                .ProbeAsync(filePath, cancellationToken)
                .ConfigureAwait(false);

            string fileHash = await _fileHasher
                .HashAsync(filePath, cancellationToken)
                .ConfigureAwait(false);

            string audioHash = await _audioHasher
                .HashAsync(filePath, cancellationToken)
                .ConfigureAwait(false);

            // ---- Soft fails: silence + loudness ----
            SilenceMeasurement silence = await TryDetectSilenceAsync(
                filePath, settings, cancellationToken).ConfigureAwait(false);

            LoudnessMeasurement? loudness = await TryMeasureLoudnessAsync(
                filePath, cancellationToken).ConfigureAwait(false);

            Track track = new()
            {
                BatchId = batchId,
                FullPath = filePath,
                FileName = Path.GetFileName(filePath),
                Format = probe.Format,
                FileSizeBytes = probe.FileSizeBytes,
                DurationMs = probe.DurationMs,
                SampleRate = probe.SampleRate,
                Channels = probe.Channels,
                BitrateKbps = probe.BitrateKbps,
                FileHash = fileHash,
                AudioHash = audioHash,
                SilenceLeadMs = silence.SilenceLeadMs,
                SilenceTailMs = silence.SilenceTailMs,
                IntegratedLufs = loudness?.IntegratedLufs,
                TruePeakDb = loudness?.TruePeakDb,
                Status = TrackStatus.Ready,
            };

            return Result.Success(track);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Hard-fail analysis error for '{Path}'. Marking corrupted.",
                filePath);
            return Result.Failure<Track>(ex.Message, ex);
        }
    }

    private async Task<SilenceMeasurement> TryDetectSilenceAsync(
        string filePath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _silenceDetector
                .DetectAsync(
                    filePath,
                    settings.SilenceThresholdDb,
                    settings.SilenceMinDurationMs,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Silence detection failed for '{Path}'; defaulting to (0, 0).",
                filePath);
            return new SilenceMeasurement(0, 0);
        }
    }

    private async Task<LoudnessMeasurement?> TryMeasureLoudnessAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _loudnessAnalyzer
                .MeasureAsync(filePath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Loudness analysis failed for '{Path}'; leaving null.",
                filePath);
            return null;
        }
    }
}
