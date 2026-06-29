namespace LAMG.Domain.Enums;

/// <summary>
/// High-level kind of work performed by a <c>Job</c>.
/// </summary>
public enum JobType
{
    /// <summary>Import folders and analyze every track.</summary>
    AnalyzeImport = 1,

    /// <summary>Plan and render mixes for a project.</summary>
    GenerateMixes = 2,
}
