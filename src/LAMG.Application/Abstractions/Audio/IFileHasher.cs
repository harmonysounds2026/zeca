namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Computes a stable hash over the raw bytes of a file.
/// Used as the cheap duplicate pre-filter.
/// </summary>
public interface IFileHasher
{
    /// <summary>
    /// Returns the lower-case hexadecimal SHA-256 of the file content.
    /// </summary>
    Task<string> HashAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
