using LAMG.Application.UseCases.DetectDuplicates;

namespace LAMG.Application.Jobs;

/// <summary>
/// Raised by <see cref="LAMG.Application.Abstractions.IJobOrchestrator"/>
/// when duplicate tracks have been detected during import and a user
/// decision is required. The orchestrator waits at
/// <see cref="LAMG.Domain.Enums.JobStage.DetectingDuplicates"/> until the
/// host calls <c>SubmitDuplicateResolutionAsync</c>.
/// </summary>
public sealed class DuplicateResolutionRequestedEventArgs : EventArgs
{
    public DuplicateResolutionRequestedEventArgs(long jobId, DuplicateDetectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        JobId = jobId;
        Report = report;
    }

    public long JobId { get; }

    public DuplicateDetectionReport Report { get; }
}
