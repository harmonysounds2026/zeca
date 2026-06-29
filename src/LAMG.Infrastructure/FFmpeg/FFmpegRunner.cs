using System.Diagnostics;
using System.Text;

using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Domain.Enums;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.FFmpeg;

/// <summary>
/// Result of a single ffmpeg invocation. <see cref="ExitCode"/> is
/// non-zero on failure; the full stderr is captured for diagnostics.
/// </summary>
public sealed record FFmpegResult(int ExitCode, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}

/// <summary>
/// Thin process wrapper around <c>ffmpeg.exe</c>. Streams stderr on a
/// background reader (ffmpeg writes most of its output there), kills
/// the process tree on cancellation, and exposes both a buffered-stderr
/// mode and a stdout-pipe mode (used by <c>AudioHasher</c> to feed
/// decoded PCM directly into SHA-256 without an intermediate file).
/// </summary>
public sealed class FFmpegRunner
{
    private readonly ICpuModeApplier _cpuModeApplier;
    private readonly ILogger<FFmpegRunner> _logger;

    public FFmpegRunner(
        ICpuModeApplier cpuModeApplier,
        ILogger<FFmpegRunner> logger)
    {
        _cpuModeApplier = Guard.NotNull(cpuModeApplier);
        _logger = Guard.NotNull(logger);
    }

    /// <summary>
    /// Runs ffmpeg with the supplied argument list. stderr is buffered
    /// and returned. Arguments are passed through
    /// <see cref="ProcessStartInfo.ArgumentList"/> to avoid shell
    /// quoting issues.
    /// </summary>
    public async Task<FFmpegResult> RunAsync(
        string ffmpegPath,
        IReadOnlyList<string> arguments,
        CpuMode cpuMode,
        Action<string>? stderrLine = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(arguments);

        using System.Diagnostics.Process process = new();
        ConfigureStartInfo(process, ffmpegPath, arguments, redirectStandardOutput: false);

        StringBuilder stderrBuffer = new();
        AttachStderrHandler(process, stderrBuffer, stderrLine);

        StartProcess(process);
        process.BeginErrorReadLine();
        _cpuModeApplier.Apply(process, cpuMode);

        await using CancellationTokenRegistration reg = RegisterKillOnCancel(process, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The kill callback handled the actual process; rethrow so
            // the orchestrator can decide what to do.
            throw;
        }

        // Ensures all buffered stderr events have fired.
        process.WaitForExit();

        string stderr = stderrBuffer.ToString();
        return new FFmpegResult(process.ExitCode, stderr);
    }

    /// <summary>
    /// Runs ffmpeg with stdout redirected to <paramref name="stdoutConsumer"/>.
    /// stderr is still buffered. Useful when the output is binary (e.g.
    /// PCM samples streamed to a hasher).
    /// </summary>
    public async Task<FFmpegResult> RunWithStdoutAsync(
        string ffmpegPath,
        IReadOnlyList<string> arguments,
        CpuMode cpuMode,
        Func<Stream, CancellationToken, Task> stdoutConsumer,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(ffmpegPath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(stdoutConsumer);

        using System.Diagnostics.Process process = new();
        ConfigureStartInfo(process, ffmpegPath, arguments, redirectStandardOutput: true);

        StringBuilder stderrBuffer = new();
        AttachStderrHandler(process, stderrBuffer, stderrLine: null);

        StartProcess(process);
        process.BeginErrorReadLine();
        _cpuModeApplier.Apply(process, cpuMode);

        await using CancellationTokenRegistration reg = RegisterKillOnCancel(process, cancellationToken);

        try
        {
            // The consumer reads from stdout. ffmpeg blocks if its
            // stdout pipe fills, so the consumer must actually read.
            await stdoutConsumer(process.StandardOutput.BaseStream, cancellationToken)
                .ConfigureAwait(false);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Drain buffered stderr lines.
            try { process.WaitForExit(); } catch (InvalidOperationException) { }
        }

        string stderr = stderrBuffer.ToString();
        return new FFmpegResult(process.ExitCode, stderr);
    }

    private static void ConfigureStartInfo(
        System.Diagnostics.Process process,
        string fileName,
        IReadOnlyList<string> arguments,
        bool redirectStandardOutput)
    {
        process.StartInfo.FileName = fileName;
        foreach (string arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = redirectStandardOutput;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = false;
        process.StartInfo.CreateNoWindow = true;
        // stdin is intentionally NOT redirected. Callers should pass
        // -nostdin to ffmpeg so it does not block waiting for input.
    }

    private static void AttachStderrHandler(
        System.Diagnostics.Process process,
        StringBuilder buffer,
        Action<string>? onLine)
    {
        process.ErrorDataReceived += (_, e) =>
        {
            string? line = e.Data;
            if (line is null)
            {
                return;
            }

            lock (buffer)
            {
                buffer.AppendLine(line);
            }

            try
            {
                onLine?.Invoke(line);
            }
            catch (Exception)
            {
                // A misbehaving observer must not bring down ffmpeg.
            }
        };
    }

    private void StartProcess(System.Diagnostics.Process process)
    {
        bool started;
        try
        {
            started = process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ffmpeg process at {Path}.", process.StartInfo.FileName);
            throw new InvalidOperationException(
                $"Failed to start ffmpeg ({process.StartInfo.FileName}): {ex.Message}", ex);
        }

        if (!started)
        {
            throw new InvalidOperationException(
                $"ffmpeg ({process.StartInfo.FileName}) failed to start.");
        }
    }

    private static CancellationTokenRegistration RegisterKillOnCancel(
        System.Diagnostics.Process process,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return default;
        }

        return cancellationToken.Register(static state =>
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
                // Process may have already exited between the HasExited
                // check and Kill; that's harmless.
            }
        }, process);
    }
}
