using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebBrowser.Models;
using WebBrowser.Services;

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

    /// <summary>True while the downloads flyout is open.</summary>
    [ObservableProperty]
    private bool _isDownloadsOpen;

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
}
