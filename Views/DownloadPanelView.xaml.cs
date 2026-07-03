using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// 下载浮层内容：<see cref="ViewModels.DownloadItemViewModel"/> 的可滚动列表，
/// 每项带进度与操作。纯 view —— 绑定 <see cref="ViewModels.MainViewModel.Downloads"/>。
/// </summary>
public partial class DownloadPanelView : UserControl
{
    public DownloadPanelView()
    {
        InitializeComponent();
    }
}
