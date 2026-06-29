using System.Buffers;
using System.Security.Cryptography;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.Audio;
using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Infrastructure.FFmpeg;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.Audio;

/// <inheritdoc cref="IAudioHasher"/>
/// <remarks>
/// Asks ffmpeg to decode the audio file to a canonical PCM format
/// (s16le, stereo, 44.1 kHz) and pipes the bytes directly into a
/// streaming SHA-256. The hash is therefore independent of the source
/// container, codec or bitrate — only the decoded content matters.
/// </remarks>
public sealed class AudioHasher : IAudioHasher
{
    private const int BufferSize = 64 * 1024;

    private readonly FFmpegRunner _ffmpegRunner;
    private readonly IFFmpegLocator _locator;
    private readonly ISettingsService _settings;
    private readonly ILogger<AudioHasher> _logger;

    public AudioHasher(
        FFmpegRunner ffmpegRunner,
        IFFmpegLocator locator,
        ISettingsService settings,
        ILogger<AudioHasher> logger)
    {
        _ffmpegRunner = Guard.NotNull(ffmpegRunner);
        _locator = Guard.NotNull(locator);
        _settings = Guard.NotNull(settings);
        _logger = Guard.NotNull(logger);
    }

    public async Task<string> HashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(filePath);

        string? ffmpegPath = _locator.GetFFmpegPath();
        if (ffmpegPath is null)
        {
            throw new InvalidOperationException("ffmpeg executable could not be located.");
        }

        byte[]? hashBytes = null;

        IReadOnlyList<string> args =
        [
            "-nostdin",
            "-hide_banner",
            "-loglevel", "error",
            "-i", filePath,
            "-vn",
            "-map", "a:0",
            "-f", "s16le",
            "-ac", "2",
            "-ar", "44100",
            "pipe:1",
        ];

        FFmpegResult result = await _ffmpegRunner.RunWithStdoutAsync(
            ffmpegPath,
            args,
            _settings.Current.CpuMode,
            async (stream, ct) =>
            {
                using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    int bytesRead;
                    while ((bytesRead = await stream
                        .ReadAsync(buffer.AsMemory(0, BufferSize), ct)
                        .ConfigureAwait(false)) > 0)
                    {
                        hasher.AppendData(buffer, 0, bytesRead);
                    }

                    hashBytes = hasher.GetHashAndReset();
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"ffmpeg failed to decode '{filePath}' (exit {result.ExitCode}): " +
                $"{result.StandardError.Trim()}");
        }

        if (hashBytes is null)
        {
            throw new InvalidOperationException(
                $"ffmpeg produced no audio data for '{filePath}'.");
        }

        string hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        _logger.LogTrace("Audio-hashed {Path} -> {Hash}.", filePath, hex);
        return hex;
    }
}
