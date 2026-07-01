using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using WebBrowser.Models;
using WebBrowser.Services;
using WebBrowser.WebView;

namespace WebBrowser.ViewModels;

/// <summary>View model for a single browser tab. Holds the <see cref="WebViewTab"/> that owns the WebView2 control.</summary>
public sealed partial class TabViewModel : ObservableObject
{
    private readonly WebViewTab _webView;

    public WebViewTab WebView => _webView;

    [ObservableProperty] private string _title = "New Tab";
    [ObservableProperty] private ImageSource? _favicon;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private TabState _state = TabState.Loaded;

    public TabViewModel(DownloadManagerService downloadManager)
    {
        _webView = new WebViewTab(this, downloadManager);
    }

    public Task InitializeAsync(CoreWebView2Environment environment)
        => _webView.InitializeAsync(environment);

    /// <summary>Navigate this tab to the given address-bar input (normalized via <see cref="UrlHelper"/>).</summary>
    public void Navigate(string? input) => _webView.Navigate(input);

    [RelayCommand] private void GoBack() => _webView.GoBack();
    [RelayCommand] private void GoForward() => _webView.GoForward();
    [RelayCommand] private void Reload() => _webView.Reload();
}
