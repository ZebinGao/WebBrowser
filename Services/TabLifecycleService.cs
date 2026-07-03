using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WebBrowser.ViewModels;

namespace WebBrowser.Services;

/// <summary>
/// 拥有标签集合及完整的标签生命周期：打开、切换、关闭（含后继选择与末标签处理）、teardown，
/// 以及共享 browser 进程死亡时的尽力恢复。<see cref="MainViewModel"/> 绑定到 <see cref="Tabs"/>
/// 并通过 <see cref="ActiveTabChanged"/> 镜像 <see cref="ActiveTab"/>。
/// </summary>
public sealed class TabLifecycleService
{
    private readonly WebViewEnvironmentService _environmentService;
    private readonly DownloadManagerService _downloadManager;
    private readonly HistoryService _history;
    private bool _isShuttingDown;

    /// <summary>每个标签的挂起计时器。一个标签在持续不活动达到此时长后被挂起。</summary>
    private readonly Dictionary<TabViewModel, CancellationTokenSource> _suspendTimers = new();
    private static readonly TimeSpan SuspendDelay = TimeSpan.FromSeconds(12);

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public TabViewModel? ActiveTab { get; private set; }

    public event EventHandler<TabViewModel?>? ActiveTabChanged;

    public TabLifecycleService(WebViewEnvironmentService environmentService, DownloadManagerService downloadManager, HistoryService history)
    {
        _environmentService = environmentService;
        _downloadManager = downloadManager;
        _history = history;
        _environmentService.EnvironmentLost += OnEnvironmentLost;
    }

    /// <summary>创建标签、激活它、基于共享 environment 初始化其 WebView2，然后导航。</summary>
    public async Task<TabViewModel> OpenTabAsync(string? url = null)
    {
        var tab = new TabViewModel(_downloadManager, _history);
        tab.WebView.NewTabRequested += OnNewTabRequested;
        Tabs.Add(tab);
        SetActive(tab);

        CoreWebView2Environment environment = await _environmentService.GetEnvironmentAsync();
        await tab.InitializeAsync(environment);

        if (!string.IsNullOrWhiteSpace(url))
            tab.Navigate(url);

        return tab;
    }

    /// <summary>页面请求了新窗口（target="_blank"）。将其路由为一个真正的应用标签。</summary>
    private void OnNewTabRequested(object? sender, string? url)
        => _ = OpenTabAsync(url);

    /// <summary>激活一个标签并取消激活其余标签。</summary>
    public void SetActive(TabViewModel? tab)
    {
        if (tab is null || ActiveTab == tab)
            return;

        TabViewModel? previous = ActiveTab;

        foreach (var existing in Tabs)
            existing.IsActive = existing == tab;

        ActiveTab = tab;
        ActiveTabChanged?.Invoke(this, tab);

        // 立即唤醒我们正切换到的标签（代价小），并对离开的标签做去抖挂起。
        _ = tab.WebView.ResumeAsync();
        if (previous is not null && previous != tab)
            _ = ScheduleSuspendAsync(previous);
    }

    /// <summary>
    /// 在 <see cref="SuspendDelay"/> 的不活动后挂起 <paramref name="tab"/>。重新调度（快速切换标签）会
    /// 取消上一个计时器。计时器触发时，仅当该标签仍不活动才真正挂起。
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
    /// 关闭一个标签：选择后继标签（优先右邻居，否则左）用于激活，移除该标签，teardown 其 WebView2，
    /// 并推动 GC 以尽快回收 renderer 进程。关闭最后一个标签会打开一个新的空白标签（与 Chrome/Edge 一致）。
    /// </summary>
    public async Task CloseTabAsync(TabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
            return;

        int index = Tabs.IndexOf(tab);
        bool wasActive = ActiveTab == tab;

        // 移除前先选好后继标签（优先右邻居，否则左）。
        TabViewModel? successor = null;
        if (wasActive && !_isShuttingDown && Tabs.Count > 1)
            successor = (index + 1 < Tabs.Count) ? Tabs[index + 1] : Tabs[index - 1];

        Tabs.Remove(tab);
        CancelSuspendTimer(tab);
        tab.WebView.NewTabRequested -= OnNewTabRequested;

        if (!_isShuttingDown)
        {
            if (successor is not null)
                SetActive(successor);
            else if (Tabs.Count == 0)
                await OpenTabAsync(); // 最后一个标签关闭 -> 打开一个新的空白标签
        }

        await tab.WebView.TearDownAsync();

        // 推动 GC，使该标签的 renderer 进程尽快释放（核心内存目标）。
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        GC.WaitForPendingFinalizers();
    }

    /// <summary>teardown 每个标签，使 renderer 进程在应用退出前被释放。</summary>
    public async Task ShutdownAsync()
    {
        _isShuttingDown = true;

        // 取消每个待处理的挂起，确保 teardown 中途不会有计时器触发。
        foreach (var cts in _suspendTimers.Values)
            cts.Cancel();
        _suspendTimers.Clear();

        var snapshot = Tabs.ToList();
        ActiveTab = null;
        ActiveTabChanged?.Invoke(this, null);

        foreach (var tab in snapshot)
        {
            try { await tab.WebView.TearDownAsync(); }
            catch { /* 忽略 */ }
        }

        Tabs.Clear();
    }

    private async void OnEnvironmentLost(object? sender, EventArgs e)
    {
        // 共享的 browser 进程已死亡；每个标签的 CoreWebView2 现在都失效。UI 线程上的尽力恢复：
        // 清除死掉的标签并打开一个新标签（这会重建 environment）。
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
            // 恢复是尽力而为；此处绝不能崩溃。
        }
    }
}
