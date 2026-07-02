using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WebBrowser.ViewModels;

namespace WebBrowser.Services;

/// <summary>
/// Owns the tab collection and the full tab lifecycle: open, switch, close (with successor selection
/// and last-tab handling), teardown, and best-effort recovery when the shared browser process dies.
/// <see cref="MainViewModel"/> binds to <see cref="Tabs"/> and mirrors <see cref="ActiveTab"/> via
/// <see cref="ActiveTabChanged"/>.
/// </summary>
public sealed class TabLifecycleService
{
    private readonly WebViewEnvironmentService _environmentService;
    private readonly DownloadManagerService _downloadManager;
    private bool _isShuttingDown;

    /// <summary>Per-tab pending suspend timers. A tab is suspended after it stays inactive this long.</summary>
    private readonly Dictionary<TabViewModel, CancellationTokenSource> _suspendTimers = new();
    private static readonly TimeSpan SuspendDelay = TimeSpan.FromSeconds(12);

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public TabViewModel? ActiveTab { get; private set; }

    public event EventHandler<TabViewModel?>? ActiveTabChanged;

    public TabLifecycleService(WebViewEnvironmentService environmentService, DownloadManagerService downloadManager)
    {
        _environmentService = environmentService;
        _downloadManager = downloadManager;
        _environmentService.EnvironmentLost += OnEnvironmentLost;
    }

    /// <summary>Creates a tab, activates it, initializes its WebView2 against the shared environment, then navigates.</summary>
    public async Task<TabViewModel> OpenTabAsync(string? url = null)
    {
        var tab = new TabViewModel(_downloadManager);
        Tabs.Add(tab);
        SetActive(tab);

        CoreWebView2Environment environment = await _environmentService.GetEnvironmentAsync();
        await tab.InitializeAsync(environment);

        if (!string.IsNullOrWhiteSpace(url))
            tab.Navigate(url);

        return tab;
    }

    /// <summary>Activates a tab and deactivates the rest.</summary>
    public void SetActive(TabViewModel? tab)
    {
        if (tab is null || ActiveTab == tab)
            return;

        TabViewModel? previous = ActiveTab;

        foreach (var existing in Tabs)
            existing.IsActive = existing == tab;

        ActiveTab = tab;
        ActiveTabChanged?.Invoke(this, tab);

        // Eagerly wake the tab we are switching to (cheap), then debounce-suspend the one we left.
        _ = tab.WebView.ResumeAsync();
        if (previous is not null && previous != tab)
            _ = ScheduleSuspendAsync(previous);
    }

    /// <summary>
    /// Suspend <paramref name="tab"/> after <see cref="SuspendDelay"/> of inactivity. Re-scheduling
    /// (rapid tab switching) cancels the previous timer. The tab is only suspended if it is still
    /// inactive when the timer fires.
    /// </summary>
    private async Task ScheduleSuspendAsync(TabViewModel tab)
    {
        if (_suspendTimers.TryGetValue(tab, out var existing))
            existing.Cancel();

        var cts = new CancellationTokenSource();
        _suspendTimers[tab] = cts;

        try
        {
            await Task.Delay(SuspendDelay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _suspendTimers.Remove(tab);
        cts.Dispose();

        if (!_isShuttingDown && !tab.IsActive && Tabs.Contains(tab))
            await tab.WebView.TrySuspendAsync();
    }

    private void CancelSuspendTimer(TabViewModel tab)
    {
        if (_suspendTimers.TryGetValue(tab, out var cts))
        {
            cts.Cancel();
            _suspendTimers.Remove(tab);
        }
    }

    /// <summary>
    /// Closes a tab: picks a successor (right neighbor, else left) for activation, removes the tab,
    /// tears down its WebView2, and nudges the GC so the renderer process is reclaimed promptly.
    /// Closing the last tab opens a fresh blank tab (matches Chrome/Edge).
    /// </summary>
    public async Task CloseTabAsync(TabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
            return;

        int index = Tabs.IndexOf(tab);
        bool wasActive = ActiveTab == tab;

        // Choose a successor before removing (right neighbor preferred, else left).
        TabViewModel? successor = null;
        if (wasActive && !_isShuttingDown && Tabs.Count > 1)
            successor = (index + 1 < Tabs.Count) ? Tabs[index + 1] : Tabs[index - 1];

        Tabs.Remove(tab);
        CancelSuspendTimer(tab);

        if (!_isShuttingDown)
        {
            if (successor is not null)
                SetActive(successor);
            else if (Tabs.Count == 0)
                await OpenTabAsync(); // last tab closed -> open a fresh blank tab
        }

        await tab.WebView.TearDownAsync();

        // Nudge the GC so this tab's renderer process is released promptly (the key memory goal).
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        GC.WaitForPendingFinalizers();
    }

    /// <summary>Tears down every tab so renderer processes are released before app exit.</summary>
    public async Task ShutdownAsync()
    {
        _isShuttingDown = true;

        // Cancel every pending suspend so no timer fires mid-teardown.
        foreach (var cts in _suspendTimers.Values)
            cts.Cancel();
        _suspendTimers.Clear();

        var snapshot = Tabs.ToList();
        ActiveTab = null;
        ActiveTabChanged?.Invoke(this, null);

        foreach (var tab in snapshot)
        {
            try { await tab.WebView.TearDownAsync(); }
            catch { /* ignore */ }
        }

        Tabs.Clear();
    }

    private async void OnEnvironmentLost(object? sender, EventArgs e)
    {
        // The shared browser process died; every tab's CoreWebView2 is now invalid. Best-effort
        // recovery on the UI thread: clear the dead tabs and open a fresh one (which recreates the env).
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                _isShuttingDown = true;
                Tabs.Clear();
                ActiveTab = null;
                _isShuttingDown = false;
                await OpenTabAsync();
            });
        }
        catch
        {
            // Recovery is best-effort; never crash here.
        }
    }
}
