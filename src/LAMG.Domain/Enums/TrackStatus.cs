namespace LAMG.Domain.Enums;

/// <summary>
/// Per-track outcome of the analysis stage.
/// </summary>
public enum TrackStatus
{
    /// <summary>Row has been created but not yet analyzed.</summary>
    Pending = 0,

    /// <summary>Analyzed successfully and eligible for mix planning.</summary>
    Ready = 1,

    /// <summary>Explicitly excluded (e.g. duplicate, user choice).</summary>
    Skipped = 2,

    /// <summary>Decoder failed; the track is permanently ineligible.</summary>
    Corrupted = 3,
}
