using LAMG.Application.Abstractions.System;
using LAMG.Common;

using Microsoft.Extensions.Logging;

namespace LAMG.Infrastructure.FileSystem;

/// <inheritdoc cref="IFileSystemService"/>
public sealed class FileSystemService : IFileSystemService
{
    private readonly ILogger<FileSystemService> _logger;

    public FileSystemService(ILogger<FileSystemService> logger)
    {
        _logger = Guard.NotNull(logger);
    }

    public void EnsureDirectory(string path)
    {
        Guard.NotNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
    }

    public long GetFreeSpaceBytes(string path)
    {
        Guard.NotNullOrWhiteSpace(path);

        try
        {
            // Normalize to absolute, then resolve the volume root.
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                // Genuinely unknown — return long.MaxValue so callers
                // don't refuse work; the eventual write will fail
                // cleanly if disk runs out.
                return long.MaxValue;
            }

            DriveInfo drive = new(root);
            return drive.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not determine free disk space for '{Path}'; assuming sufficient space.",
                path);
            return long.MaxValue;
        }
    }

    public async Task AtomicReplaceAsync(
        string tempPath,
        string finalPath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(tempPath);
        Guard.NotNullOrWhiteSpace(finalPath);

        if (!File.Exists(tempPath))
        {
            throw new FileNotFoundException(
                $"Atomic replace source not found: {tempPath}",
                tempPath);
        }

        // Refuse to publish an empty file — a zero-byte mp3 or wav is
        // almost always the result of ffmpeg crashing mid-write.
        long size = new FileInfo(tempPath).Length;
        if (size == 0)
        {
            throw new InvalidOperationException(
                $"Atomic replace source is empty: {tempPath}");
        }

        string? destFolder = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(destFolder))
        {
            Directory.CreateDirectory(destFolder);
        }

        // Retry on transient IO locks (AV scans, indexer, etc.).
        const int MaxAttempts = 6;
        int delayMs = 100;
        Exception? lastError = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // File.Move with overwrite=true is atomic on the same
                // volume on Windows (uses MoveFileEx with REPLACE_EXISTING).
                File.Move(tempPath, finalPath, overwrite: true);
                _logger.LogDebug(
                    "Atomically replaced '{Final}' (size {Bytes}).",
                    finalPath, size);
                return;
            }
            catch (Exception ex) when (
                ex is IOException
                  or UnauthorizedAccessException
                && attempt < MaxAttempts)
            {
                lastError = ex;
                _logger.LogDebug(
                    "Atomic rename attempt {Attempt}/{Max} failed: {Message}. Retrying in {Delay}ms.",
                    attempt, MaxAttempts, ex.Message, delayMs);

                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                delayMs = Math.Min(delayMs * 2, 2000);
            }
        }

        throw new IOException(
            $"Atomic rename of '{tempPath}' to '{finalPath}' failed after {MaxAttempts} attempts.",
            lastError);
    }

    public string BuildOutputPath(string folder, int indexInProject, string extension, DateOnly date)
    {
        Guard.NotNullOrWhiteSpace(folder);
        Guard.Positive(indexInProject);
        Guard.NotNullOrWhiteSpace(extension);

        // Strip any leading dot from the extension and normalize case.
        string ext = extension.TrimStart('.').ToLowerInvariant();
        string fileName = $"{date:yyyy-MM-dd}_mix_{indexInProject:000}.{ext}";
        return Path.Combine(folder, fileName);
    }
}
