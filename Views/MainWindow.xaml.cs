using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WebBrowser.ViewModels;
using Wpf.Ui.Controls;

namespace WebBrowser.Views;

/// <summary>
/// Fluent shell. Hosts the title bar, toolbar/address bar, and the overlaid tab content region.
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
    /// FluentWindow.OnClosing is synchronous, but tearing down WebView2 tabs is async.
    /// Pattern: on the first close, cancel it and run async teardown, then Close() again — the
    /// <see cref="_isClosing"/> guard lets the second pass through. The <c>Task.Yield</c> is essential:
    /// it lets WPF finish aborting the first close (resetting its internal closing flag) before we
    /// initiate the second, otherwise Close() throws "window is closing".
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
            // Swallow teardown errors so the window still closes.
        }

        await Task.Yield();
        Close();
    }
}
