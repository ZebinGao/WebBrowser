using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// 书签浮层内容：<see cref="WebBrowser.Models.Bookmark"/> 的可滚动列表，每行带打开操作与移除按钮。
/// 纯 view —— 绑定到 <see cref="ViewModels.MainViewModel.Bookmarks"/>。
/// </summary>
public partial class BookmarksPanelView : UserControl
{
    public BookmarksPanelView()
    {
        InitializeComponent();
    }
}
