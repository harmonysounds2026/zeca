using System.Diagnostics;

using LAMG.Common;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.FFmpeg;

/// <summary>
/// Thin wrapper around <c>ffprobe.exe</c> that returns the JSON output
/// produced by <c>-print_format json -show_streams -show_format</c>.
/// Higher-level parsing lives in
/// <see cref="LAMG.Infrastructure.Audio.FFprobeService"/>.
/// </summary>
public sealed class FFprobeRunner
{
    private readonly ILogger<FFprobeRunner> _logger;

    public FFprobeRunner(ILogger<FFprobeRunner> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    /// <summary>
    /// Runs ffprobe against the supplied file path and returns the
    /// captured stdout (JSON document).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when ffprobe exits with a non-zero code. The exception
    /// message includes the trimmed stderr for diagnostics.
    /// </exception>
    public async Task<string> RunAsync(
        string ffprobePath,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(ffprobePath);
        Guard.NotNullOrWhiteSpace(filePath);

        using System.Diagnostics.Process process = new();
        process.StartInfo.FileName = ffprobePath;
        process.StartInfo.ArgumentList.Add("-v");
        process.StartInfo.ArgumentList.Add("error");
        process.StartInfo.ArgumentList.Add("-hide_banner");
        process.StartInfo.ArgumentList.Add("-print_format");
        process.StartInfo.ArgumentList.Add("json");
        process.StartInfo.ArgumentList.Add("-show_streams");
        process.StartInfo.ArgumentList.Add("-show_format");
        process.StartInfo.ArgumentList.Add(filePath);

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Failed to start ffprobe ({ffprobePath}).");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to start ffprobe at {Path}.", ffprobePath);
            throw new InvalidOperationException(
                $"Failed to start ffprobe ({ffprobePath}): {ex.Message}", ex);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await using CancellationTokenRegistration reg = cancellationToken.Register(static state =>
        {
            System.Diagnostics.Process p = (System.Diagnostics.Process)state!;
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Race: process exited concurrently.
            }
        }, process);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            string trimmed = stderr.Trim();
            throw new InvalidOperationException(
                $"ffprobe exited with code {process.ExitCode} for '{filePath}': {trimmed}");
        }

        return stdout;
    }
}
