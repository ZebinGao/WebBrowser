using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WebBrowser.Services;
using WebBrowser.ViewModels;
using WebBrowser.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

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
        services.AddSingleton<DownloadManagerService>();
        services.AddSingleton<HistoryService>();
        services.AddSingleton<BookmarksService>();
        services.AddSingleton<TabLifecycleService>();
        services.AddSingleton<MainViewModel>();
        _services = services.BuildServiceProvider();

        var mainViewModel = _services.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow { DataContext = mainViewModel };
        mainWindow.Show();

        // Match the OS dark/light theme so Mica and controls blend with the system.
        bool systemDark = !IsSystemLightTheme();
        ApplicationThemeManager.Apply(
            systemDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true);
        mainViewModel.IsDarkTheme = systemDark;

        // Open the initial tab once the window is on screen (fire-and-forget, errors surfaced).
        _ = ShowInitialTabAsync(mainViewModel);
    }

    /// <summary>Reads the user's "AppsUseLightTheme" registry value (0 = dark, 1 = light).</summary>
    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ShowInitialTabAsync(MainViewModel mainViewModel)
    {
        try
        {
            await mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "Failed to initialize the browser engine:\n\n" + ex.Message,
                "WebBrowser",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            // Flush any pending history/bookmark writes before the process exits.
            _services.GetRequiredService<HistoryService>().Dispose();
            _services.GetRequiredService<BookmarksService>().Dispose();
            _services.GetRequiredService<WebViewEnvironmentService>().Dispose();
        }

        base.OnExit(e);
    }
}
