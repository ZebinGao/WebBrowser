using System.Windows.Controls;

namespace WebBrowser.Views;

/// <summary>
/// 水平标签条：<see cref="ItemsControl"/> 形式的标签列表（每项为带关闭 overlay 的选择按钮）
/// 外加一个"新建标签"按钮。纯 view —— 所有交互通过绑定到 <see cref="ViewModels.MainViewModel"/> 的命令完成。
/// </summary>
public partial class TabStripView : UserControl
{
    public TabStripView()
    {
        InitializeComponent();
    }
}
