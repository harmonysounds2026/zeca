using LAMG.Application.Abstractions.Audio;

namespace LAMG.Application.UseCases.DetectDuplicates;

/// <summary>
/// Summary of duplicate detection across the imported batches.
/// </summary>
public sealed record DuplicateDetectionReport(
    IReadOnlyList<DuplicateGroup> Groups)
{
    public bool HasDuplicates => Groups.Count > 0;
}
