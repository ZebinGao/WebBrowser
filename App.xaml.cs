using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WebBrowser.Services;
using WebBrowser.ViewModels;
using WebBrowser.Views;

namespace WebBrowser;

/// <summary>
/// Application entry point. Wires up DI (singleton services + main view model), shows the main window,
/// and disposes the shared WebView2 environment on exit.
/// </summary>
public partial class App : Application
{
    private IServiceProvider _services = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<WebViewEnvironmentService>();
        services.AddSingleton<MainViewModel>();
        _services = services.BuildServiceProvider();

        var mainViewModel = _services.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow { DataContext = mainViewModel };
        mainWindow.Show();

        // Open the initial tab once the window is on screen (fire-and-forget, errors surfaced).
        _ = ShowInitialTabAsync(mainViewModel);
    }

    private static async Task ShowInitialTabAsync(MainViewModel mainViewModel)
    {
        try
        {
            await mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Failed to initialize the browser engine:\n\n" + ex.Message,
                "WebBrowser",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
            _services.GetRequiredService<WebViewEnvironmentService>().Dispose();

        base.OnExit(e);
    }
}
