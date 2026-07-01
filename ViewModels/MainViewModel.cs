using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebBrowser.Services;

namespace WebBrowser.ViewModels;

/// <summary>
/// Top-level view model. Binds the tab collection and active tab from <see cref="TabLifecycleService"/>,
/// and exposes commands for new/close/select tab plus active-tab navigation. The lifecycle service owns
/// all the open/close/teardown orchestration.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly TabLifecycleService _lifecycle;
    private readonly WebViewEnvironmentService _environmentService;

    /// <summary>URL the initial tab opens on launch. New blank tabs navigate to about:blank.</summary>
    private const string DefaultStartUrl = "https://www.bing.com";

    public ObservableCollection<TabViewModel> Tabs => _lifecycle.Tabs;

    [ObservableProperty]
    private TabViewModel? _activeTab;

    /// <summary>Live count of WebView2 runtime processes (browser/GPU/network/renderer). Debug aid for teardown verification.</summary>
    [ObservableProperty]
    private int _webViewProcessCount;

    public MainViewModel(WebViewEnvironmentService environmentService, TabLifecycleService lifecycle)
    {
        _environmentService = environmentService;
        _lifecycle = lifecycle;

        _lifecycle.ActiveTabChanged += (_, tab) => ActiveTab = tab;
        _environmentService.ProcessCountChanged += (_, _) => WebViewProcessCount = _environmentService.ProcessInfos.Count;
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
}
