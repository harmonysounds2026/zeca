namespace LAMG.Domain.Enums;

/// <summary>
/// Strategy chosen by the user when duplicates are detected during import.
/// </summary>
public enum DuplicateResolution
{
    /// <summary>Keep every duplicate; flag them in the log.</summary>
    ImportAll = 1,

    /// <summary>Mark new duplicates as <see cref="TrackStatus.Skipped"/>.</summary>
    SkipDuplicates = 2,

    /// <summary>Replace previously imported copies with the new ones.</summary>
    ReplaceExisting = 3,
}
