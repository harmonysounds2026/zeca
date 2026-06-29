using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using LAMG.Domain.Models;

namespace LAMG.UI.ViewModels.Dialogs;

/// <summary>
/// Backing for the modal shown between the Unique-mode and Reuse-mode
/// phases. The user toggles batches in/out of the reuse pool; the
/// dialog code-behind reads the resulting selection on Accept. No
/// commands here — buttons are wired through Click handlers in
/// code-behind so the result is captured deterministically before the
/// window closes (same reason as the duplicate-resolution dialog).
/// </summary>
public sealed class ReusePoolSelectionDialogViewModel
{
    public ReusePoolSelectionDialogViewModel(IReadOnlyList<Batch> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);

        foreach (Batch batch in batches)
        {
            // Default: every batch checked. Users uncheck what they
            // don't want in the reuse pool.
            Batches.Add(new ReusePoolBatchRow
            {
                BatchId = batch.Id,
                SourceFolder = batch.SourceFolder,
                TrackCount = batch.TrackCount,
                IsSelected = true,
            });
        }
    }

    public ObservableCollection<ReusePoolBatchRow> Batches { get; } = [];
}

/// <summary>
/// One row in the reuse-pool dialog. <see cref="IsSelected"/> is bound
/// two-way to a DataGrid checkbox column.
/// </summary>
public sealed partial class ReusePoolBatchRow : ObservableObject
{
    public long BatchId { get; init; }
    public string SourceFolder { get; init; } = string.Empty;
    public int TrackCount { get; init; }

    [ObservableProperty]
    private bool _isSelected;
}
