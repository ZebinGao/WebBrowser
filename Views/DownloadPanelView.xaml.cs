using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// Downloads flyout content: a scrollable list of <see cref="ViewModels.DownloadItemViewModel"/> with
/// per-item progress and actions. Pure view — binds to <see cref="ViewModels.MainViewModel.Downloads"/>.
/// </summary>
public partial class DownloadPanelView : UserControl
{
    public DownloadPanelView()
    {
        InitializeComponent();
    }
}
