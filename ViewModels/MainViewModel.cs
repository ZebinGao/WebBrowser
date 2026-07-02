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
/// Top-level view model. Binds the tab collection and active tab from <see cref="TabLifecycleService"/>,
/// exposes tab commands, and surfaces the download manager, history, and bookmarks (live lists,
/// panel toggles, and the star-button state). The lifecycle service owns all open/close/teardown
/// orchestration; history is recorded by each tab's <see cref="WebBrowser.WebView.WebViewTab"/> on
/// navigation completion.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly TabLifecycleService _lifecycle;
    private readonly WebViewEnvironmentService _environmentService;
    private readonly DownloadManagerService _downloadService;
    private readonly HistoryService _history;
    private readonly BookmarksService _bookmarks;

    /// <summary>URL the initial tab opens on launch. New blank tabs navigate to about:blank.</summary>
    private const string DefaultStartUrl = "https://www.bing.com";

    public ObservableCollection<TabViewModel> Tabs => _lifecycle.Tabs;

    public ObservableCollection<DownloadItemViewModel> Downloads => _downloadService.Items;

    /// <summary>Browsing history, newest first. Bound by the history panel.</summary>
    public ObservableCollection<HistoryEntry> History => _history.Entries;

    /// <summary>Bookmarks, newest first. Bound by the bookmarks panel.</summary>
    public ObservableCollection<Bookmark> Bookmarks => _bookmarks.Items;

    [ObservableProperty]
    private TabViewModel? _activeTab;

    /// <summary>Live count of WebView2 runtime processes (browser/GPU/network/renderer). Debug aid.</summary>
    [ObservableProperty]
    private int _webViewProcessCount;

    /// <summary>Total working set (MB) of the WebView2 processes backing this app — drops when background tabs suspend.</summary>
    [ObservableProperty]
    private int _webViewMemoryMb;

    /// <summary>True while the downloads flyout is open.</summary>
    [ObservableProperty]
    private bool _isDownloadsOpen;

    /// <summary>True while the history flyout is open.</summary>
    [ObservableProperty]
    private bool _isHistoryOpen;

    /// <summary>True while the bookmarks flyout is open.</summary>
    [ObservableProperty]
    private bool _isBookmarksOpen;

    /// <summary>Current dark/light theme — drives the toggle button's icon and applied theme.</summary>
    [ObservableProperty]
    private bool _isDarkTheme = true;

    /// <summary>True when the active tab's URL is already bookmarked — drives the star icon (filled vs outline).</summary>
    [ObservableProperty]
    private bool _isCurrentPageBookmarked;

    /// <summary>Number of downloads currently in progress or paused — drives the toolbar badge.</summary>
    public int ActiveDownloadCount
    {
        get => _activeDownloadCount;
        private set => SetProperty(ref _activeDownloadCount, value);
    }
    private int _activeDownloadCount;

    /// <summary>The tab whose PropertyChanged we are currently subscribed to (for star-state tracking).</summary>
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

        // Poll total WebView2 memory every few seconds so the suspend/resume effect is visible.
        var memoryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        memoryTimer.Tick += (_, _) => RefreshWebViewMemory();
        memoryTimer.Start();
        RefreshWebViewMemory();

        _downloadService.Items.CollectionChanged += OnDownloadsCollectionChanged;
        _downloadService.RetryRequested += (_, uri) => ActiveTab?.Navigate(uri.ToString());

        // Adding/removing a bookmark may flip the current page's bookmarked state.
        _bookmarks.Items.CollectionChanged += (_, _) => RecomputeBookmarkState();
    }

    /// <summary>Called after the main window is shown — opens the initial tab.</summary>
    public Task InitializeAsync() => _lifecycle.OpenTabAsync(DefaultStartUrl);

    /// <summary>Navigate the active tab to the given address-bar input.</summary>
    public void NavigateActive(string? input) => ActiveTab?.Navigate(input);

    /// <summary>Tear down every tab so renderer processes are released before exit.</summary>
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

    /// <summary>Star button: bookmark the active page, or un-bookmark it if already saved.</summary>
    [RelayCommand]
    private void ToggleCurrentBookmark()
    {
        if (ActiveTab is null || !IsHttpUrl(ActiveTab.Address))
            return;
        _bookmarks.Toggle(ActiveTab.Address, ActiveTab.Title);
        RecomputeBookmarkState();
    }

    /// <summary>Open a history entry in the active tab.</summary>
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

    /// <summary>Open a bookmark in the active tab.</summary>
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

    // --- Star-button state: recompute when the active tab or its address changes ---

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

    /// <summary>Sum the working set of every WebView2 process the shared environment reports.</summary>
    private void RefreshWebViewMemory()
    {
        try
        {
            long total = 0;
            foreach (var info in _environmentService.ProcessInfos)
            {
                try { total += Process.GetProcessById((int)info.ProcessId).WorkingSet64; }
                catch { /* process exited between snapshot and query */ }
            }
            WebViewMemoryMb = (int)(total / (1024 * 1024));
            WebViewProcessCount = _environmentService.ProcessInfos.Count;
        }
        catch
        {
            // Environment not ready yet — retried on the next tick.
        }
    }
}
