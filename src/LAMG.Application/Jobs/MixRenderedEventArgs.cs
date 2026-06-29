using LAMG.Domain.Enums;

namespace LAMG.Application.Jobs;

/// <summary>
/// Raised by <see cref="LAMG.Application.Abstractions.IJobOrchestrator"/>
/// after each mix render completes (either successfully or with a
/// failure). The Processing screen subscribes to this to append rows
/// to its "completed mixes" list as work happens, rather than polling
/// the database.
/// </summary>
public sealed class MixRenderedEventArgs : EventArgs
{
    public MixRenderedEventArgs(
        long jobId,
        long mixId,
        int indexInProject,
        MixMode mode,
        MixStatus status,
        OutputFormat outputFormat,
        string? outputPath,
        int actualDurationSeconds,
        string? error)
    {
        JobId = jobId;
        MixId = mixId;
        IndexInProject = indexInProject;
        Mode = mode;
        Status = status;
        OutputFormat = outputFormat;
        OutputPath = outputPath;
        ActualDurationSeconds = actualDurationSeconds;
        Error = error;
    }

    public long JobId { get; }

    public long MixId { get; }

    public int IndexInProject { get; }

    public MixMode Mode { get; }

    /// <summary>
    /// Terminal status of the mix. Either
    /// <see cref="MixStatus.Completed"/> or <see cref="MixStatus.Failed"/>.
    /// </summary>
    public MixStatus Status { get; }

    public OutputFormat OutputFormat { get; }

    /// <summary>Absolute path of the rendered file; null when failed.</summary>
    public string? OutputPath { get; }

    public int ActualDurationSeconds { get; }

    /// <summary>Failure message; null when successful.</summary>
    public string? Error { get; }
}
