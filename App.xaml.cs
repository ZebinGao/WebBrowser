using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WebBrowser.Services;
using WebBrowser.ViewModels;
using WebBrowser.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WebBrowser;

/// <summary>
/// 应用入口。装配 DI（单例服务 + 主 view model），显示主窗口，并在退出时释放共享的 WebView2 environment。
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

        // 跟随系统 dark/light 主题，使 Mica 与控件和系统融为一体。
        bool systemDark = !IsSystemLightTheme();
        ApplicationThemeManager.Apply(
            systemDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true);
        mainViewModel.IsDarkTheme = systemDark;

        // 窗口上屏后打开初始标签（fire-and-forget，错误会冒泡呈现）。
        _ = ShowInitialTabAsync(mainViewModel);
    }

    /// <summary>读取用户的 "AppsUseLightTheme" 注册表值（0 = dark，1 = light）。</summary>
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
            // 进程退出前，把待写入的历史/书签刷盘。
            _services.GetRequiredService<HistoryService>().Dispose();
            _services.GetRequiredService<BookmarksService>().Dispose();
            _services.GetRequiredService<WebViewEnvironmentService>().Dispose();
        }

        base.OnExit(e);
    }
}
