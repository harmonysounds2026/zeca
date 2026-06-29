namespace LAMG.Domain.Models;

/// <summary>
/// One track placed at a specific position inside a mix, with the
/// crossfade durations chosen by the planner.
/// </summary>
public sealed class MixItem
{
    public long Id { get; set; }

    public long MixId { get; set; }

    public long TrackId { get; set; }

    /// <summary>0-based position in the mix.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Effective duration after silence trim (ms).</summary>
    public int TrimmedMs { get; set; }

    /// <summary>Crossfade-in duration applied to this track (ms).</summary>
    public int XfadeInMs { get; set; }

    /// <summary>Crossfade-out duration applied to this track (ms).</summary>
    public int XfadeOutMs { get; set; }
}
