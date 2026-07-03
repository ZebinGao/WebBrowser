using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebBrowser.Models;
using WebBrowser.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WebBrowser.ViewModels;

/// <summary>
/// 顶层 view model。绑定来自 <see cref="TabLifecycleService"/> 的标签集合与活动标签，暴露标签命令，
/// 并呈现 download manager、history 与 bookmarks（实时列表、面板开关以及星标按钮状态）。
/// 生命周期服务拥有所有打开/关闭/teardown 的编排；history 在每次导航完成时由各标签的
/// <see cref="WebBrowser.WebView.WebViewTab"/> 记录。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly TabLifecycleService _lifecycle;
    private readonly WebViewEnvironmentService _environmentService;
    private readonly DownloadManagerService _downloadService;
    private readonly HistoryService _history;
    private readonly BookmarksService _bookmarks;

    /// <summary>初始标签在启动时打开的 URL。新空白标签导航到 about:blank。</summary>
    private const string DefaultStartUrl = "https://www.bing.com";

    public ObservableCollection<TabViewModel> Tabs => _lifecycle.Tabs;

    public ObservableCollection<DownloadItemViewModel> Downloads => _downloadService.Items;

    /// <summary>浏览历史，最新的在最前。由历史面板绑定。</summary>
    public ObservableCollection<HistoryEntry> History => _history.Entries;

    /// <summary>书签，最新的在最前。由书签面板绑定。</summary>
    public ObservableCollection<Bookmark> Bookmarks => _bookmarks.Items;

    [ObservableProperty]
    private TabViewModel? _activeTab;

    /// <summary>WebView2 运行时进程的实时数量（browser/GPU/network/renderer）。调试辅助。</summary>
    [ObservableProperty]
    private int _webViewProcessCount;

    /// <summary>支撑本应用的 WebView2 进程总 working set（MB）—— 后台标签挂起时会下降。</summary>
    [ObservableProperty]
    private int _webViewMemoryMb;

    /// <summary>下载浮层打开时为 true。</summary>
    [ObservableProperty]
    private bool _isDownloadsOpen;

    /// <summary>历史浮层打开时为 true。</summary>
    [ObservableProperty]
    private bool _isHistoryOpen;

    /// <summary>书签浮层打开时为 true。</summary>
    [ObservableProperty]
    private bool _isBookmarksOpen;

    /// <summary>当前 dark/light 主题 —— 驱动切换按钮的图标与所应用的主题。</summary>
    [ObservableProperty]
    private bool _isDarkTheme = true;

    /// <summary>当活动标签的 URL 已被收藏时为 true —— 驱动星标图标（实心 vs 空心）。</summary>
    [ObservableProperty]
    private bool _isCurrentPageBookmarked;

    /// <summary>当前正在进行或暂停的下载条数 —— 驱动工具栏角标。</summary>
    public int ActiveDownloadCount
    {
        get => _activeDownloadCount;
        private set => SetProperty(ref _activeDownloadCount, value);
    }
    private int _activeDownloadCount;

    /// <summary>当前正在订阅其 PropertyChanged 的标签（用于跟踪星标状态）。</summary>
    private TabViewModel? _bookmarkedTab;

    public MainViewModel(
        WebViewEnvironmentService environmentService,
        TabLifecycleService lifecycle,
        DownloadManagerService downloadService,
        HistoryService history,
        BookmarksService bookmarks)
    {
        _environmentService = environmentService;
        _lifecycle = lifecycle;
        _downloadService = downloadService;
        _history = history;
        _bookmarks = bookmarks;

        _lifecycle.ActiveTabChanged += (_, tab) => ActiveTab = tab;
        _environmentService.ProcessCountChanged += (_, _) => WebViewProcessCount = _environmentService.ProcessInfos.Count;

        // 每隔几秒轮询一次 WebView2 总内存，让挂起/恢复的效果可见。
        var memoryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        memoryTimer.Tick += (_, _) => RefreshWebViewMemory();
        memoryTimer.Start();
        RefreshWebViewMemory();

        _downloadService.Items.CollectionChanged += OnDownloadsCollectionChanged;
        _downloadService.RetryRequested += (_, uri) => ActiveTab?.Navigate(uri.ToString());

        // 增删书签可能翻转当前页面的收藏状态。
        _bookmarks.Items.CollectionChanged += (_, _) => RecomputeBookmarkState();
    }

    /// <summary>在主窗口显示后调用 —— 打开初始标签。</summary>
    public Task InitializeAsync() => _lifecycle.OpenTabAsync(DefaultStartUrl);

    /// <summary>把活动标签导航到给定的地址栏输入。</summary>
    public void NavigateActive(string? input) => ActiveTab?.Navigate(input);

    /// <summary>teardown 每个标签，使 renderer 进程在退出前被释放。</summary>
    public Task ShutdownAsync() => _lifecycle.ShutdownAsync();

    [RelayCommand]
    private Task NewTabAsync() => _lifecycle.OpenTabAsync();

    [RelayCommand]
    private Task CloseTabAsync(TabViewModel? tab) => _lifecycle.CloseTabAsync(tab);

    [RelayCommand]
    private void SelectTab(TabViewModel? tab) => _lifecycle.SetActive(tab);

    [RelayCommand]
    private void ToggleDownloads() => IsDownloadsOpen = !IsDownloadsOpen;

    [RelayCommand]
    private void ToggleHistory() => IsHistoryOpen = !IsHistoryOpen;

    [RelayCommand]
    private void ToggleBookmarks() => IsBookmarksOpen = !IsBookmarksOpen;

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(
            IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true);
    }

    /// <summary>星标按钮：收藏当前页面，若已收藏则取消收藏。</summary>
    [RelayCommand]
    private void ToggleCurrentBookmark()
    {
        if (ActiveTab is null || !IsHttpUrl(ActiveTab.Address))
            return;
        _bookmarks.Toggle(ActiveTab.Address, ActiveTab.Title);
        RecomputeBookmarkState();
    }

    /// <summary>在活动标签中打开一条历史记录。</summary>
    [RelayCommand]
    private void OpenHistoryEntry(HistoryEntry? entry) => OpenUrl(entry?.Url);

    [RelayCommand]
    private void RemoveHistoryEntry(HistoryEntry? entry)
    {
        if (entry is not null)
            _history.Remove(entry);
    }

    [RelayCommand]
    private void ClearHistory() => _history.Clear();

    /// <summary>在活动标签中打开一个书签。</summary>
    [RelayCommand]
    private void OpenBookmark(Bookmark? bookmark) => OpenUrl(bookmark?.Url);

    [RelayCommand]
    private void RemoveBookmark(Bookmark? bookmark)
    {
        if (bookmark is not null)
            _bookmarks.Remove(bookmark);
    }

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        ActiveTab?.Navigate(url);
    }

    private static bool IsHttpUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // --- 星标按钮状态：当活动标签或其地址变化时重算 ---

    partial void OnActiveTabChanged(TabViewModel? value)
    {
        if (_bookmarkedTab is not null)
            _bookmarkedTab.PropertyChanged -= OnActiveTabPropertyChanged;
        _bookmarkedTab = value;
        if (value is not null)
            value.PropertyChanged += OnActiveTabPropertyChanged;
        RecomputeBookmarkState();
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.Address))
            RecomputeBookmarkState();
    }

    private void RecomputeBookmarkState()
        => IsCurrentPageBookmarked = ActiveTab is not null && _bookmarks.ContainsUrl(ActiveTab.Address);

    private void OnDownloadsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (DownloadItemViewModel item in e.NewItems)
                item.PropertyChanged += OnItemPropertyChanged;

        if (e.OldItems is not null)
            foreach (DownloadItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnItemPropertyChanged;

        RecomputeActiveCount();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadItemViewModel.State))
            RecomputeActiveCount();
    }

    private void RecomputeActiveCount()
    {
        int count = 0;
        foreach (var item in _downloadService.Items)
        {
            if (item.State == DownloadState.InProgress || item.State == DownloadState.Paused)
                count++;
        }
        ActiveDownloadCount = count;
    }

    /// <summary>对共享 environment 上报的每个 WebView2 进程的 working set 求和。</summary>
    private void RefreshWebViewMemory()
    {
        try
        {
            long total = 0;
            foreach (var info in _environmentService.ProcessInfos)
            {
                try { total += Process.GetProcessById((int)info.ProcessId).WorkingSet64; }
                catch { /* 进程在快照与查询之间退出 */ }
            }
            WebViewMemoryMb = (int)(total / (1024 * 1024));
            WebViewProcessCount = _environmentService.ProcessInfos.Count;
        }
        catch
        {
            // environment 尚未就绪 —— 下一个 tick 会重试。
        }
    }
}
