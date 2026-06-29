namespace LAMG.Application.Abstractions.Audio;

/// <summary>
/// Reason a set of tracks were considered duplicates by the detector.
/// </summary>
public enum DuplicateMatchKind
{
    /// <summary>Same file name (case-insensitive) inside the project.</summary>
    FileName = 1,

    /// <summary>Same SHA-256 of the raw file bytes.</summary>
    FileHash = 2,

    /// <summary>Same SHA-256 of the decoded PCM stream.</summary>
    AudioHash = 3,
}

/// <summary>
/// A set of tracks that match each other under a single duplicate rule.
/// The first id is the existing track; subsequent ids are newly imported
/// duplicates of it. Implementations may also group across new tracks
/// only when no existing reference exists.
/// </summary>
public sealed record DuplicateGroup(
    DuplicateMatchKind Kind,
    IReadOnlyList<long> TrackIds);
