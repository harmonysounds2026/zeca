using System.Collections.ObjectModel;

using LAMG.Application.Abstractions.Audio;
using LAMG.Application.UseCases.DetectDuplicates;

namespace LAMG.UI.ViewModels.Dialogs;

/// <summary>
/// Backing for the duplicate-resolution modal. The dialog records the
/// user's choice on its own <c>SelectedResolution</c> property; this
/// view-model only exposes the read-only list of conflicting groups.
/// </summary>
public sealed class DuplicateResolutionDialogViewModel
{
    public DuplicateResolutionDialogViewModel(DuplicateDetectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        foreach (DuplicateGroup group in report.Groups)
        {
            Groups.Add(new DuplicateGroupRow(
                group.Kind,
                string.Join(", ", group.TrackIds)));
        }
    }

    public ObservableCollection<DuplicateGroupRow> Groups { get; } = [];
}

public sealed record DuplicateGroupRow(DuplicateMatchKind Kind, string TrackIds);
