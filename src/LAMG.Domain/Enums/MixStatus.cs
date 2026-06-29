namespace LAMG.Domain.Enums;

/// <summary>
/// Lifecycle status of a single planned or rendered mix.
/// </summary>
public enum MixStatus
{
    Planned = 0,
    Rendering = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}
