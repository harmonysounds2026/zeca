using System.Windows;

using LAMG.Common;
using LAMG.UI.ViewModels.Dialogs;

namespace LAMG.UI.Views.Dialogs;

/// <summary>
/// Modal that asks the user which batches contribute to the reuse-mode
/// pool. <see cref="SelectedBatchIds"/> exposes the user's choice after
/// <see cref="Window.ShowDialog"/> returns: a non-null collection on
/// Accept (possibly empty if the user unchecked everything), or
/// <c>null</c> on Cancel.
/// </summary>
/// <remarks>
/// Buttons are wired Click-only (no MVVM Command binding) so the
/// result is set before the window closes — WPF raises Click before
/// the bound Command, which would otherwise close the dialog with a
/// stale or default result.
/// </remarks>
public partial class ReusePoolSelectionDialog : Window
{
    private readonly ReusePoolSelectionDialogViewModel _viewModel;

    public ReusePoolSelectionDialog(ReusePoolSelectionDialogViewModel viewModel)
    {
        _viewModel = Guard.NotNull(viewModel);
        DataContext = _viewModel;
        InitializeComponent();
    }

    /// <summary>
    /// Selected batch ids after the dialog closes. <c>null</c> means
    /// the user cancelled; an empty collection means the user
    /// explicitly opted out (every checkbox cleared).
    /// </summary>
    public IReadOnlyCollection<long>? SelectedBatchIds { get; private set; }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        List<long> selected = new();
        foreach (ReusePoolBatchRow row in _viewModel.Batches)
        {
            if (row.IsSelected)
            {
                selected.Add(row.BatchId);
            }
        }

        SelectedBatchIds = selected;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        SelectedBatchIds = null;
        DialogResult = false;
        Close();
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        foreach (ReusePoolBatchRow row in _viewModel.Batches)
        {
            row.IsSelected = true;
        }
    }

    private void OnSelectNone(object sender, RoutedEventArgs e)
    {
        foreach (ReusePoolBatchRow row in _viewModel.Batches)
        {
            row.IsSelected = false;
        }
    }
}
