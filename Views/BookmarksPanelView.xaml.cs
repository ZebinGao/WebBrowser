using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// Bookmarks flyout content: a scrollable list of <see cref="WebBrowser.Models.Bookmark"/> with a
/// per-row open action and remove button. Pure view — binds to <see cref="ViewModels.MainViewModel.Bookmarks"/>.
/// </summary>
public partial class BookmarksPanelView : UserControl
{
    public BookmarksPanelView()
    {
        InitializeComponent();
    }
}
