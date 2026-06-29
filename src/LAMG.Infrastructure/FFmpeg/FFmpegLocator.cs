using System.Diagnostics;

using LAMG.Application.Abstractions;
using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Infrastructure.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LAMG.Infrastructure.FFmpeg;

/// <inheritdoc cref="IFFmpegLocator"/>
public sealed class FFmpegLocator : IFFmpegLocator
{
    private readonly InfrastructureOptions _options;
    private readonly ISettingsService _settings;
    private readonly ILogger<FFmpegLocator> _logger;

    public FFmpegLocator(
        IOptions<InfrastructureOptions> options,
        ISettingsService settings,
        ILogger<FFmpegLocator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = Guard.NotNull(options.Value);
        _settings = Guard.NotNull(settings);
        _logger = Guard.NotNull(logger);
    }

    public string? GetFFmpegPath() => ResolveExecutable(GetFFmpegExeName());

    public string? GetFFprobePath() => ResolveExecutable(GetFFprobeExeName());

    public async Task<bool> VerifyAvailableAsync(CancellationToken cancellationToken = default)
    {
        string? ffmpeg = GetFFmpegPath();
        string? ffprobe = GetFFprobePath();

        if (ffmpeg is null)
        {
            _logger.LogWarning("ffmpeg executable could not be located.");
            return false;
        }

        if (ffprobe is null)
        {
            _logger.LogWarning("ffprobe executable could not be located.");
            return false;
        }

        bool ffmpegOk = await RunVersionCheckAsync(ffmpeg, cancellationToken).ConfigureAwait(false);
        bool ffprobeOk = await RunVersionCheckAsync(ffprobe, cancellationToken).ConfigureAwait(false);

        if (ffmpegOk && ffprobeOk)
        {
            _logger.LogInformation(
                "FFmpeg available. ffmpeg='{FFmpeg}', ffprobe='{FFprobe}'.",
                ffmpeg, ffprobe);
        }

        return ffmpegOk && ffprobeOk;
    }

    private string? ResolveExecutable(string exeName)
    {
        // 1. Bundled folder (preferred).
        if (!string.IsNullOrWhiteSpace(_options.FFmpegBundledFolder))
        {
            string candidate = Path.Combine(_options.FFmpegBundledFolder, exeName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        // 2. User-configured override folder.
        string? overrideFolder = _settings.Current.FFmpegPathOverride;
        if (!string.IsNullOrWhiteSpace(overrideFolder))
        {
            try
            {
                string candidate = Path.Combine(overrideFolder, exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Invalid ffmpeg override path '{Path}'.", overrideFolder);
            }
        }

        // 3. System PATH.
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // Malformed PATH segment - skip silently.
                }
            }
        }

        return null;
    }

    private async Task<bool> RunVersionCheckAsync(
        string exePath,
        CancellationToken cancellationToken)
    {
        try
        {
            using System.Diagnostics.Process process = new();
            process.StartInfo.FileName = exePath;
            process.StartInfo.ArgumentList.Add("-version");
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            if (!process.Start())
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Version check failed for '{Path}'.", exePath);
            return false;
        }
    }

    private static string GetFFmpegExeName()
        => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    private static string GetFFprobeExeName()
        => OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
}
