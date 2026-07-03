using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WebBrowser.ViewModels;
using Wpf.Ui.Controls;

namespace WebBrowser.Views;

/// <summary>
/// Fluent 外壳。承载标题栏、工具栏/地址栏，以及 overlay 的标签内容区。
/// </summary>
public partial class MainWindow : FluentWindow
{
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            vm.NavigateActive(AddressBox.Text);
            e.Handled = true;
        }
    }

    /// <summary>
    /// FluentWindow.OnClosing 是同步的，但拆卸 WebView2 标签是异步的。
    /// 模式：首次关闭时先取消并跑异步拆卸，再 Close() 一次 —— <see cref="_isClosing"/> 守卫让第二次放行。
    /// <c>Task.Yield</c> 至关重要：它让 WPF 完成对首次关闭的中止（重置其内部 closing 标志）后，
    /// 我们再发起第二次，否则 Close() 抛 "window is closing"。
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_isClosing)
        {
            base.OnClosing(e);
            return;
        }

        _isClosing = true;
        e.Cancel = true;

        try
        {
            if (DataContext is MainViewModel vm)
                await vm.ShutdownAsync();
        }
        catch
        {
            // 吞掉拆卸错误，确保窗口仍能关闭。
        }

        await Task.Yield();
        Close();
    }
}
