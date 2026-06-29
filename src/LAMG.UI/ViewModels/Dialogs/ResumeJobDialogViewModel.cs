using LAMG.Common;
using LAMG.Domain.Models;

namespace LAMG.UI.ViewModels.Dialogs;

/// <summary>
/// Read-only backing for the modal shown at startup when a previously
/// interrupted job is detected. The user's choice is captured by the
/// dialog's code-behind (Click-only pattern), not via commands, so
/// this view-model only exposes the job summary.
/// </summary>
public sealed class ResumeJobDialogViewModel
{
    public ResumeJobDialogViewModel(Job job)
    {
        Job = Guard.NotNull(job);
    }

    public Job Job { get; }

    public string Summary => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"Job #{Job.Id} ({Job.JobType}) - stage {Job.CurrentStage}, status {Job.Status}, " +
        $"last heartbeat {Job.LastHeartbeat:yyyy-MM-dd HH:mm:ss}");
}
