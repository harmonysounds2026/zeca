namespace LAMG.Application.Abstractions.System;

/// <summary>
/// File-system primitives that the application layer relies on:
/// atomic writes, free-space checks, and long-path-safe path utilities.
/// </summary>
public interface IFileSystemService
{
    /// <summary>Creates the directory if it does not already exist.</summary>
    void EnsureDirectory(string path);

    /// <summary>
    /// Returns the free disk space (bytes) available on the volume
    /// containing <paramref name="path"/>.
    /// </summary>
    long GetFreeSpaceBytes(string path);

    /// <summary>
    /// Atomically replaces <paramref name="finalPath"/> with the file
    /// currently at <paramref name="tempPath"/>. Retries on transient
    /// IO failures (e.g. AV file locks).
    /// </summary>
    Task AtomicReplaceAsync(
        string tempPath,
        string finalPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a unique output filename in <paramref name="folder"/>
    /// using the pattern <c>YYYY-MM-DD_mix_{index:000}.{ext}</c>. The
    /// index automatically increments to avoid collisions.
    /// </summary>
    string BuildOutputPath(string folder, int indexInProject, string extension, DateOnly date);
}
