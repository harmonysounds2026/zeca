using LAMG.Domain.Enums;

namespace LAMG.Domain.Models;

/// <summary>
/// A single planned or rendered long-form mix.
/// </summary>
public sealed class Mix
{
    public long Id { get; set; }

    public long ProjectId { get; set; }

    /// <summary>1-based index used in the output filename suffix.</summary>
    public int IndexInProject { get; set; }

    /// <summary>Target duration requested by the user (minutes).</summary>
    public int TargetMin { get; set; }

    /// <summary>Achieved duration after rendering (seconds).</summary>
    public int ActualSec { get; set; }

    public MixMode Mode { get; set; }

    public OutputFormat OutputFormat { get; set; }

    /// <summary>Absolute path to the rendered file (null until completed).</summary>
    public string? OutputPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public MixStatus Status { get; set; } = MixStatus.Planned;
}
