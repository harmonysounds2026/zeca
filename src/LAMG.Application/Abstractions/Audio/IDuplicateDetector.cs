using LAMG.Domain.Models;

namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Finds duplicate groups inside a set of freshly analyzed tracks
/// using filename, file hash, and audio hash. No fingerprinting is
/// performed in v1.
/// </summary>
public interface IDuplicateDetector
{
    /// <param name="candidates">
    /// The newly imported tracks. Must already have <c>FileName</c>,
    /// <c>FileHash</c> and <c>AudioHash</c> populated.
    /// </param>
    /// <param name="existing">
    /// Previously imported tracks in the same project, used as the
    /// reference set for matching.
    /// </param>
    Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        IReadOnlyList<Track> candidates,
        IReadOnlyList<Track> existing,
        CancellationToken cancellationToken = default);
}
