using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// 历史浮层内容：<see cref="WebBrowser.Models.HistoryEntry"/> 的可滚动列表（最新的在最前），
/// 每行带打开/移除，外加一个全部清除操作。纯 view —— 绑定到 <see cref="ViewModels.MainViewModel.History"/>。
/// </summary>
public partial class HistoryPanelView : UserControl
{
    public HistoryPanelView()
    {
        InitializeComponent();
    }
}
