using CommunityToolkit.Mvvm.ComponentModel;

namespace LAMG.UI.ViewModels;

/// <summary>
/// One row in the Import screen's batch list. Bindable so progress
/// updates appear without refreshing the entire grid.
/// </summary>
public sealed partial class BatchFolderRow : ObservableObject
{
    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private string _status = "Pending";

    [ObservableProperty]
    private double _progressFraction;

    /// <summary>
    /// Database id of the persisted <c>Batch</c> row, once import
    /// has produced one. <c>null</c> until then.
    /// </summary>
    public long? BatchId { get; set; }
}
