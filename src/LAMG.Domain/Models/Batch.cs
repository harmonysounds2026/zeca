namespace LAMG.Domain.Models;

/// <summary>
/// One imported folder of source audio tracks. A batch is the unit of
/// scope for <see cref="Enums.MixMode.Unique"/> mixes: every Unique
/// mix uses tracks from exactly one batch.
/// </summary>
public sealed class Batch
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>Absolute path of the folder that was imported.</summary>
    public string SourceFolder { get; set; } = string.Empty;

    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Number of tracks discovered in this batch (analysis status independent).</summary>
    public int TrackCount { get; set; }
}
