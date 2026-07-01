using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Web.WebView2.Core;
using WebBrowser.Services;

namespace WebBrowser.ViewModels;

/// <summary>
/// Top-level view model. Owns the tab collection and the active-tab pointer.
/// For M1 it manages a single tab end-to-end; M2 adds the full open/close/switch/suspend lifecycle.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly WebViewEnvironmentService _environmentService;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    /// <summary>URL the initial tab opens on launch. New blank tabs (M2) navigate to about:blank.</summary>
    private const string DefaultStartUrl = "https://www.bing.com";

    [ObservableProperty]
    private TabViewModel? _activeTab;

    public MainViewModel(WebViewEnvironmentService environmentService)
    {
        _environmentService = environmentService;
    }

    /// <summary>Called after the main window is shown — opens the initial tab.</summary>
    public async Task InitializeAsync()
    {
        if (Tabs.Count == 0)
            await OpenTabAsync(DefaultStartUrl);
    }

    public async Task OpenTabAsync(string? url = null)
    {
        var tab = new TabViewModel();
        Tabs.Add(tab);
        SetActive(tab);

        // Realizing the tab's WebView2 into the visual tree happens on binding; InitializeAsync
        // awaits the control's Loaded event, so order (add -> init) is safe.
        CoreWebView2Environment environment = await _environmentService.GetEnvironmentAsync();
        await tab.InitializeAsync(environment);

        if (!string.IsNullOrWhiteSpace(url))
            tab.Navigate(url);
    }

    public void SetActive(TabViewModel tab)
    {
        foreach (var existing in Tabs)
            existing.IsActive = existing == tab;

        ActiveTab = tab;
    }

    /// <summary>Navigate the active tab to the given address-bar input.</summary>
    public void NavigateActive(string? input) => ActiveTab?.Navigate(input);

    /// <summary>Tear down every tab so renderer processes are released before exit.</summary>
    public async Task ShutdownAsync()
    {
        var snapshot = Tabs.ToList();
        ActiveTab = null;
        foreach (var tab in snapshot)
        {
            try { await tab.WebView.TearDownAsync(); }
            catch { /* ignore */ }
        }

        Tabs.Clear();
    }
}
