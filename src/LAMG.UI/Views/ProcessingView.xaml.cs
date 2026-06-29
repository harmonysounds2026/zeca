using System.Windows.Controls;

namespace LAMG.UI.Views;

/// <summary>
/// View for the Processing screen. Combines status &amp; controls,
/// live logs, and the completed-mixes list in one screen — no
/// separate Logs or Results screens in v1.
/// </summary>
public partial class ProcessingView : UserControl
{
    public ProcessingView()
    {
        InitializeComponent();
    }
}
