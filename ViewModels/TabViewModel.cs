using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Web.WebView2.Core;
using WebBrowser.Models;
using WebBrowser.Services;
using WebBrowser.WebView;

namespace WebBrowser.ViewModels;

/// <summary>单个浏览器标签的 view model。持有拥有 WebView2 控件的 <see cref="WebViewTab"/>。</summary>
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

    public TabViewModel(DownloadManagerService downloadManager, HistoryService history)
    {
        _webView = new WebViewTab(this, downloadManager, history);
    }

    public Task InitializeAsync(CoreWebView2Environment environment)
        => _webView.InitializeAsync(environment);

    /// <summary>把本标签导航到给定的地址栏输入（经 <see cref="UrlHelper"/> 规范化）。</summary>
    public void Navigate(string? input) => _webView.Navigate(input);

    [RelayCommand] private void GoBack() => _webView.GoBack();
    [RelayCommand] private void GoForward() => _webView.GoForward();
    [RelayCommand] private void Reload() => _webView.Reload();
}
