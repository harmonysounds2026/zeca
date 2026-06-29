namespace LAMG.Domain.Enums;

/// <summary>
/// Fine-grained stage within a job. Used to display progress and to
/// resume the correct step after a crash.
/// </summary>
public enum JobStage
{
    NotStarted = 0,
    Importing = 1,
    Analyzing = 2,
    DetectingDuplicates = 3,
    PlanningUniqueMixes = 4,
    RenderingUniqueMixes = 5,
    AwaitingReusePool = 6,
    PlanningReuseMixes = 7,
    RenderingReuseMixes = 8,
    Finalizing = 9,
    Done = 10,
}
