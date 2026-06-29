using System.Windows;

using LAMG.Common;
using LAMG.UI.ViewModels;

namespace LAMG.UI.Views;

/// <summary>
/// Main application window. The shell holds the sidebar and a
/// content area that swaps between the three child views via
/// implicit DataTemplates declared in App.xaml.
/// </summary>
public partial class ShellWindow : Window
{
    public ShellWindow(ShellViewModel viewModel)
    {
        DataContext = Guard.NotNull(viewModel);
        InitializeComponent();
    }
}
