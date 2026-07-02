using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// History flyout content: a scrollable list of <see cref="WebBrowser.Models.HistoryEntry"/>
/// (newest first) with per-row open/remove and a Clear-all action. Pure view — binds to
/// <see cref="ViewModels.MainViewModel.History"/>.
/// </summary>
public partial class HistoryPanelView : UserControl
{
    public HistoryPanelView()
    {
        InitializeComponent();
    }
}
