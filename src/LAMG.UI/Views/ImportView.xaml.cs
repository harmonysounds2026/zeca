using System.Windows.Controls;

namespace LAMG.UI.Views;

/// <summary>
/// View for the Import screen. DataContext is supplied by the implicit
/// DataTemplate in <c>App.xaml</c> (the corresponding
/// <c>ImportViewModel</c>).
/// </summary>
public partial class ImportView : UserControl
{
    public ImportView()
    {
        InitializeComponent();
    }
}
