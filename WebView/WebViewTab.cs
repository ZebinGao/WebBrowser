using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WebBrowser.Helpers;
using WebBrowser.Services;
using WebBrowser.ViewModels;

namespace WebBrowser.WebView;

/// <summary>
/// Owns a single <see cref="WebView2"/> control instance for the lifetime of one tab, plus every
/// event subscription tied to it. The view layer "adopts" the already-built control via data binding
/// (its <see cref="Control"/> is hosted in a ContentPresenter) — the control is never recreated, which
/// is what keeps tab switching instant and avoids reparenting/HWND churn.
/// </summary>
public sealed class WebViewTab
{
    private readonly TabViewModel _vm;
    private readonly DownloadManagerService _downloadManager;
    private bool _initialized;
    private bool _tornDown;

    public WebView2 Control { get; }

    public CoreWebView2 Core =>
        Control.CoreWebView2 ?? throw new InvalidOperationException("WebView2 core has not been initialized yet.");

    public bool IsInitialized => _initialized;
    public bool IsSuspended { get; private set; }

    public WebViewTab(TabViewModel vm, DownloadManagerService downloadManager)
    {
        _vm = vm;
        _downloadManager = downloadManager;
        Control = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    /// <summary>
    /// Creates the CoreWebView2 controller for this control against the shared environment.
    /// The WPF control needs to be loaded into the visual tree (have a parent HWND) before its
    /// controller can initialize, so we await <see cref="FrameworkElement.Loaded"/> if needed.
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment environment)
    {
        if (_initialized)
            return;

        if (!Control.IsLoaded)
        {
            var loaded = new TaskCompletionSource<bool>();
            void OnLoaded(object s, RoutedEventArgs e)
            {
                Control.Loaded -= OnLoaded;
                loaded.TrySetResult(true);
            }
            Control.Loaded += OnLoaded;
            await loaded.Task;
        }

        await Control.EnsureCoreWebView2Async(environment);
        ConfigureSettings();
        HookEvents();
        _initialized = true;
    }

    private void ConfigureSettings()
    {
        var settings = Core.Settings;
        settings.AreDevToolsEnabled = true;          // personal-use browser: keep devtools
        settings.IsBuiltInErrorPageEnabled = true;
        settings.IsZoomControlEnabled = false;       // we own the chrome
        settings.IsStatusBarEnabled = false;         // we surface status elsewhere
        settings.AreBrowserAcceleratorKeysEnabled = true; // Ctrl+W etc. reach the host
        // Default download bar is suppressed in M3 by handling DownloadStarting; left enabled here.
    }

    private void HookEvents()
    {
        Core.NavigationStarting += OnNavigationStarting;
        Core.NavigationCompleted += OnNavigationCompleted;
        Core.SourceChanged += OnSourceChanged;
        Core.HistoryChanged += OnHistoryChanged;
        Core.DocumentTitleChanged += OnDocumentTitleChanged;

        _downloadManager.Register(Core);
    }

    private void UnhookEvents()
    {
        if (Control.CoreWebView2 is null)
            return;

        Core.NavigationStarting -= OnNavigationStarting;
        Core.NavigationCompleted -= OnNavigationCompleted;
        Core.SourceChanged -= OnSourceChanged;
        Core.HistoryChanged -= OnHistoryChanged;
        Core.DocumentTitleChanged -= OnDocumentTitleChanged;

        _downloadManager.Unregister(Core);
    }

    // WPF WebView2 raises these on the UI thread, so no dispatcher marshaling needed.
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        => _vm.IsLoading = true;

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        => _vm.IsLoading = false;

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        => _vm.Address = Core.Source;

    private void OnHistoryChanged(object? sender, object e)
    {
        _vm.CanGoBack = Core.CanGoBack;
        _vm.CanGoForward = Core.CanGoForward;
    }

    private void OnDocumentTitleChanged(object? sender, object e)
        => _vm.Title = string.IsNullOrWhiteSpace(Core.DocumentTitle) ? Core.Source : Core.DocumentTitle;

    public void Navigate(string? input)
    {
        if (!_initialized)
            return;
        Core.Navigate(UrlHelper.Normalize(input));
    }

    public void GoBack()
    {
        if (_initialized && Core.CanGoBack)
            Core.GoBack();
    }

    public void GoForward()
    {
        if (_initialized && Core.CanGoForward)
            Core.GoForward();
    }

    public void Reload()
    {
        if (_initialized)
            Core.Reload();
    }

    /// <summary>
    /// Ordered teardown that releases this tab's renderer process. Order matters; each step is
    /// fault-tolerant so a dead Core (e.g. after <c>BrowserProcessExited</c>) doesn't block shutdown.
    /// </summary>
    public async Task TearDownAsync()
    {
        if (_tornDown)
            return;
        _tornDown = true;

        try { UnhookEvents(); }
        catch { /* ignore */ }

        try
        {
            // A suspended control must be resumed before disposal or it can hang (M5 enforces this too).
            if (Control.CoreWebView2 is not null && IsSuspended)
                await ResumeAsync();
        }
        catch { /* ignore */ }

        // The WPF WebView2 is an HwndHost: Dispose() destroys the host window, which closes the
        // CoreWebView2Controller and tears down this tab's renderer process.
        try { Control.Dispose(); }
        catch { /* ignore */ }
    }

    // --- M5 hooks (suspend/resume) — implemented in M5, no-ops for now ---

    public Task TrySuspendAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
}
