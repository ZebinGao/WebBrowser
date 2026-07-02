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
/// exposes tab commands, and surfaces the download manager (live list, active-count badge, panel toggle,
/// retry). The lifecycle service owns all open/close/teardown orchestration.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly TabLifecycleService _lifecycle;
    private readonly WebViewEnvironmentService _environmentService;
    private readonly DownloadManagerService _downloadService;

    /// <summary>URL the initial tab opens on launch. New blank tabs navigate to about:blank.</summary>
    private const string DefaultStartUrl = "https://www.bing.com";

    public ObservableCollection<TabViewModel> Tabs => _lifecycle.Tabs;

    public ObservableCollection<DownloadItemViewModel> Downloads => _downloadService.Items;

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

    /// <summary>Current dark/light theme — drives the toggle button's icon and applied theme.</summary>
    [ObservableProperty]
    private bool _isDarkTheme = true;

    /// <summary>Number of downloads currently in progress or paused — drives the toolbar badge.</summary>
    public int ActiveDownloadCount
    {
        get => _activeDownloadCount;
        private set => SetProperty(ref _activeDownloadCount, value);
    }
    private int _activeDownloadCount;

    public MainViewModel(
        WebViewEnvironmentService environmentService,
        TabLifecycleService lifecycle,
        DownloadManagerService downloadService)
    {
        _environmentService = environmentService;
        _lifecycle = lifecycle;
        _downloadService = downloadService;

        _lifecycle.ActiveTabChanged += (_, tab) => ActiveTab = tab;
        _environmentService.ProcessCountChanged += (_, _) => WebViewProcessCount = _environmentService.ProcessInfos.Count;

        // Poll total WebView2 memory every few seconds so the suspend/resume effect is visible.
        var memoryTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        memoryTimer.Tick += (_, _) => RefreshWebViewMemory();
        memoryTimer.Start();
        RefreshWebViewMemory();

        _downloadService.Items.CollectionChanged += OnDownloadsCollectionChanged;
        _downloadService.RetryRequested += (_, uri) => ActiveTab?.Navigate(uri.ToString());
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
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(
            IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light,
            WindowBackdropType.Mica,
            true);
    }

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
