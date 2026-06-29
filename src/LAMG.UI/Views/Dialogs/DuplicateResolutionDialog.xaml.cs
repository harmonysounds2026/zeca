using System.Windows;

using LAMG.Common;
using LAMG.Domain.Enums;
using LAMG.UI.ViewModels.Dialogs;

namespace LAMG.UI.Views.Dialogs;

/// <summary>
/// Modal that asks the user how to handle duplicate tracks. The chosen
/// resolution is exposed on <see cref="SelectedResolution"/>; the
/// caller (<c>DialogService</c>) reads it after <c>ShowDialog</c>
/// returns. <c>null</c> means the user cancelled.
/// </summary>
/// <remarks>
/// Each button is wired via Click only (no MVVM Command binding).
/// WPF raises Click <em>before</em> the bound Command, so combining
/// the two caused the dialog to close before the result was set —
/// hence this dialog keeps the decision in code-behind, where the
/// order is deterministic.
/// </remarks>
public partial class DuplicateResolutionDialog : Window
{
    public DuplicateResolutionDialog(DuplicateResolutionDialogViewModel viewModel)
    {
        DataContext = Guard.NotNull(viewModel);
        InitializeComponent();
    }

    /// <summary>
    /// Set when one of the action buttons is clicked. <c>null</c> when
    /// the user closed the dialog without choosing.
    /// </summary>
    public DuplicateResolution? SelectedResolution { get; private set; }

    private void OnImportAll(object sender, RoutedEventArgs e)
        => CloseWith(DuplicateResolution.ImportAll);

    private void OnSkipDuplicates(object sender, RoutedEventArgs e)
        => CloseWith(DuplicateResolution.SkipDuplicates);

    private void OnReplaceExisting(object sender, RoutedEventArgs e)
        => CloseWith(DuplicateResolution.ReplaceExisting);

    private void OnCancel(object sender, RoutedEventArgs e)
        => CloseWith(null);

    private void CloseWith(DuplicateResolution? result)
    {
        SelectedResolution = result;
        DialogResult = result.HasValue;
        Close();
    }
}
