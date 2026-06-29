using System.Windows;

using LAMG.Common;
using LAMG.UI.Services;
using LAMG.UI.ViewModels.Dialogs;

namespace LAMG.UI.Views.Dialogs;

/// <summary>
/// Modal that surfaces an interrupted job from a previous run. The
/// user's pick is exposed on <see cref="SelectedChoice"/>; the caller
/// (<c>DialogService</c>) reads it after <c>ShowDialog</c> returns.
/// </summary>
/// <remarks>
/// Buttons are wired Click-only (no MVVM Command binding). WPF raises
/// Click <em>before</em> the bound Command, which would otherwise
/// close the dialog before the result was set. Cancel is the default
/// outcome to keep the job resumable on the next launch when the user
/// closes the dialog without choosing.
/// </remarks>
public partial class ResumeJobDialog : Window
{
    public ResumeJobDialog(ResumeJobDialogViewModel viewModel)
    {
        DataContext = Guard.NotNull(viewModel);
        InitializeComponent();
    }

    public ResumeJobChoice SelectedChoice { get; private set; } = ResumeJobChoice.Cancel;

    private void OnResume(object sender, RoutedEventArgs e)
        => CloseWith(ResumeJobChoice.Resume);

    private void OnDiscard(object sender, RoutedEventArgs e)
        => CloseWith(ResumeJobChoice.Discard);

    private void OnCancel(object sender, RoutedEventArgs e)
        => CloseWith(ResumeJobChoice.Cancel);

    private void CloseWith(ResumeJobChoice choice)
    {
        SelectedChoice = choice;
        DialogResult = choice != ResumeJobChoice.Cancel;
        Close();
    }
}
