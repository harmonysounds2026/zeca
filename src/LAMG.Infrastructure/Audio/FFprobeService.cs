using System.Globalization;
using System.Text.Json;

using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.Infrastructure.FFmpeg;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="IFFprobeService"/>
/// <remarks>
/// Parses the JSON document returned by <see cref="FFprobeRunner"/>.
/// The shape is intentionally tolerant: numeric values may come back as
/// strings or numbers, and not every codec populates every field.
/// </remarks>
public sealed class FFprobeService : IFFprobeService
{
    private readonly FFprobeRunner _runner;
    private readonly IFFmpegLocator _locator;
    private readonly ILogger<FFprobeService> _logger;

    public FFprobeService(
        FFprobeRunner runner,
        IFFmpegLocator locator,
        ILogger<FFprobeService> logger)
    {
        _runner = Guard.NotNull(runner);
        _locator = Guard.NotNull(locator);
        _logger = Guard.NotNull(logger);
    }

    public async Task<ProbeResult> ProbeAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(filePath);

        string? ffprobePath = _locator.GetFFprobePath();
        if (ffprobePath is null)
        {
            throw new InvalidOperationException("ffprobe executable could not be located.");
        }

        string json = await _runner.RunAsync(ffprobePath, filePath, cancellationToken)
            .ConfigureAwait(false);

        return ParseProbe(filePath, json);
    }

    private static ProbeResult ParseProbe(string filePath, string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        JsonElement audioStream = FindFirstAudioStream(root, filePath);
        JsonElement format = root.TryGetProperty("format", out JsonElement fmtEl)
            ? fmtEl
            : default;

        // Prefer stream-level duration; fall back to format-level.
        double durationSec = ReadDouble(audioStream, "duration")
                          ?? ReadDouble(format, "duration")
                          ?? 0.0;
        long durationMs = durationSec > 0 ? (long)Math.Round(durationSec * 1000) : 0;

        int sampleRate = ReadInt(audioStream, "sample_rate") ?? 0;
        int channels = ReadInt(audioStream, "channels") ?? 0;

        // Bitrate is optional; ffprobe reports bps, we store kbps.
        long? bps = ReadLong(audioStream, "bit_rate") ?? ReadLong(format, "bit_rate");
        int? bitrateKbps = bps is > 0 ? (int)(bps.Value / 1000) : null;

        long fileSizeBytes = ReadLong(format, "size") ?? GetFileSizeFromDisk(filePath);

        string codecName = ReadString(audioStream, "codec_name") ?? string.Empty;
        string formatName = ReadString(format, "format_name") ?? string.Empty;

        AudioFormat audioFormat = DetectFormat(codecName, formatName, filePath);

        return new ProbeResult(
            audioFormat,
            durationMs,
            sampleRate,
            channels,
            bitrateKbps,
            fileSizeBytes);
    }

    private static JsonElement FindFirstAudioStream(JsonElement root, string filePath)
    {
        if (!root.TryGetProperty("streams", out JsonElement streams)
            || streams.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"ffprobe output has no 'streams' array for '{filePath}'.");
        }

        foreach (JsonElement stream in streams.EnumerateArray())
        {
            if (stream.TryGetProperty("codec_type", out JsonElement type)
                && string.Equals(type.GetString(), "audio", StringComparison.Ordinal))
            {
                return stream;
            }
        }

        throw new InvalidOperationException(
            $"No audio stream found in '{filePath}'. File may be corrupted or not audio.");
    }

    private static AudioFormat DetectFormat(string codecName, string formatName, string filePath)
    {
        if (codecName.Contains("mp3", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFormat.Mp3;
        }

        if (codecName.StartsWith("pcm", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFormat.Wav;
        }

        if (formatName.Contains("mp3", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFormat.Mp3;
        }

        if (formatName.Contains("wav", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFormat.Wav;
        }

        // Fall back to the file extension (we only enumerate .mp3/.wav).
        string ext = Path.GetExtension(filePath);
        return string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase)
            ? AudioFormat.Mp3
            : AudioFormat.Wav;
    }

    // ---------- JSON helpers (tolerant of string-vs-number) ----------

    private static double? ReadDouble(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(property, out JsonElement el)) return null;

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out double n) => n,
            JsonValueKind.String when double.TryParse(
                el.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed) => parsed,
            _ => null,
        };
    }

    private static long? ReadLong(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(property, out JsonElement el)) return null;

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out long n) => n,
            JsonValueKind.String when long.TryParse(
                el.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long parsed) => parsed,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement parent, string property)
    {
        long? value = ReadLong(parent, property);
        if (value is null) return null;
        if (value < int.MinValue || value > int.MaxValue) return null;
        return (int)value.Value;
    }

    private static string? ReadString(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(property, out JsonElement el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static long GetFileSizeFromDisk(string filePath)
    {
        try
        {
            return new FileInfo(filePath).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
