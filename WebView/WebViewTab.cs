using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WebBrowser.Helpers;
using WebBrowser.Models;
using WebBrowser.Services;
using WebBrowser.ViewModels;

namespace WebBrowser.WebView;

/// <summary>
/// 拥有某个标签整个生命周期内的单个 <see cref="WebView2"/> 控件实例，以及绑定在它上面的所有事件订阅。
/// view 层通过数据绑定“领养”这个已构建好的控件（其 <see cref="Control"/> 托管在 ContentPresenter 中）
/// —— 控件从不被重建，这正是让标签切换瞬时完成、并避免 reparent/HWND 抖动的关键。
/// </summary>
public sealed class WebViewTab
{
    private readonly TabViewModel _vm;
    private readonly DownloadManagerService _downloadManager;
    private readonly HistoryService _history;
    private bool _initialized;
    private bool _tornDown;

    /// <summary>按 host 缓存的 favicon，避免每次站内导航都重新拉取/解码。</summary>
    private static readonly Dictionary<string, ImageSource> FaviconCache = new();

    public WebView2 Control { get; }

    public CoreWebView2 Core =>
        Control.CoreWebView2 ?? throw new InvalidOperationException("WebView2 core has not been initialized yet.");

    public bool IsInitialized => _initialized;
    public bool IsSuspended { get; private set; }

    /// <summary>
    /// 当页面请求新窗口（<c>target="_blank"</c> / <c>window.open()</c>）且带 http(s) URL 时触发。
    /// 标签本身只取消裸的 Edge 弹窗；具体怎么做（打开一个新的应用标签）由生命周期服务决定，
    /// 从而让本类与标签集合保持解耦。
    /// </summary>
    public event EventHandler<string?>? NewTabRequested;

    public WebViewTab(TabViewModel vm, DownloadManagerService downloadManager, HistoryService history)
    {
        _vm = vm;
        _downloadManager = downloadManager;
        _history = history;
        Control = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    /// <summary>
    /// 基于共享 environment 为本控件创建 CoreWebView2 controller。WPF 控件必须先被加载进
    /// 可视树（拥有 parent HWND）后其 controller 才能初始化，因此必要时会等待 <see cref="FrameworkElement.Loaded"/>。
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment environment)
    {
        if (_initialized)
            return;

        if (!Control.IsLoaded)
        {
            var loaded = new TaskCompletionSource<bool>();
            void OnLoaded(object s, RoutedEventArgs e)
            {
                Control.Loaded -= OnLoaded;
                loaded.TrySetResult(true);
            }
            Control.Loaded += OnLoaded;
            await loaded.Task;
        }

        await Control.EnsureCoreWebView2Async(environment);
        ConfigureSettings();
        HookEvents();
        _initialized = true;
    }

    private void ConfigureSettings()
    {
        var settings = Core.Settings;
        settings.AreDevToolsEnabled = true;          // 个人用浏览器：保留 devtools
        settings.IsBuiltInErrorPageEnabled = true;
        settings.IsZoomControlEnabled = false;       // chrome 由我们接管
        settings.IsStatusBarEnabled = false;         // 状态信息我们另处呈现
        settings.AreBrowserAcceleratorKeysEnabled = true; // Ctrl+W 等可达宿主
        // 默认下载条已在 M3 通过处理 DownloadStarting 抑制；此处保持启用。
    }

    private void HookEvents()
    {
        Core.NavigationStarting += OnNavigationStarting;
        Core.NavigationCompleted += OnNavigationCompleted;
        Core.SourceChanged += OnSourceChanged;
        Core.HistoryChanged += OnHistoryChanged;
        Core.DocumentTitleChanged += OnDocumentTitleChanged;
        Core.FaviconChanged += OnFaviconChanged;
        Core.NewWindowRequested += OnNewWindowRequested;

        _downloadManager.Register(Core);
    }

    private void UnhookEvents()
    {
        if (Control.CoreWebView2 is null)
            return;

        Core.NavigationStarting -= OnNavigationStarting;
        Core.NavigationCompleted -= OnNavigationCompleted;
        Core.SourceChanged -= OnSourceChanged;
        Core.HistoryChanged -= OnHistoryChanged;
        Core.DocumentTitleChanged -= OnDocumentTitleChanged;
        Core.FaviconChanged -= OnFaviconChanged;
        Core.NewWindowRequested -= OnNewWindowRequested;

        _downloadManager.Unregister(Core);
    }

    // WPF WebView2 在 UI 线程上引发这些事件，因此无需 dispatcher 转发。
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        => _vm.IsLoading = true;

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _vm.IsLoading = false;

        // 把成功的 http(s) 导航记录进历史。跳过 about:/data:/blob: 以及失败的加载；
        // Core.Source 是 WebView2 规范化后的 URL，可作稳定的去重键。
        if (e.IsSuccess
            && Uri.TryCreate(Core.Source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var title = string.IsNullOrWhiteSpace(Core.DocumentTitle) ? Core.Source : Core.DocumentTitle;
            _history.Record(Core.Source, title);
        }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        => _vm.Address = Core.Source;

    private void OnHistoryChanged(object? sender, object e)
    {
        _vm.CanGoBack = Core.CanGoBack;
        _vm.CanGoForward = Core.CanGoForward;
    }

    private void OnDocumentTitleChanged(object? sender, object e)
        => _vm.Title = string.IsNullOrWhiteSpace(Core.DocumentTitle) ? Core.Source : Core.DocumentTitle;

    private async void OnFaviconChanged(object? sender, object e)
    {
        try
        {
            string host = Uri.TryCreate(Core.Source, UriKind.Absolute, out var uri) ? uri.Host : Core.Source;

            if (FaviconCache.TryGetValue(host, out var cached))
            {
                _vm.Favicon = cached;
                return;
            }

            using var stream = await Core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // 与流解绑，以便我们能 dispose 它
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze(); // 跨线程安全

            FaviconCache[host] = bitmap;
            _vm.Favicon = bitmap;
        }
        catch
        {
            // 没有 favicon 或解码失败 —— 留用默认（null）图标。
        }
    }

    // target="_blank" / window.open() 否则会弹出一个没有应用 chrome 的裸 Edge 窗口。
    // 拦截 web URL，交给生命周期服务去开一个规范的应用标签。非 web scheme
    //（mailto:、tel:、自定义协议）则放行，由 OS 默认处理程序接管。
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            e.Handled = true;
            NewTabRequested?.Invoke(this, uri.AbsoluteUri);
        }
    }

    public void Navigate(string? input)
    {
        if (!_initialized)
            return;
        Core.Navigate(UrlHelper.Normalize(input));
    }

    public void GoBack()
    {
        if (_initialized && Core.CanGoBack)
            Core.GoBack();
    }

    public void GoForward()
    {
        if (_initialized && Core.CanGoForward)
            Core.GoForward();
    }

    public void Reload()
    {
        if (_initialized)
            Core.Reload();
    }

    /// <summary>
    /// 有序的 teardown，释放本标签的 renderer 进程。顺序很重要；每一步都容错，
    /// 这样即使 Core 已死（例如 <c>BrowserProcessExited</c> 之后）也不会阻塞关闭。
    /// </summary>
    public async Task TearDownAsync()
    {
        if (_tornDown)
            return;
        _tornDown = true;

        try { UnhookEvents(); }
        catch { /* 忽略 */ }

        try
        {
            // 已挂起的控件在 dispose 前必须先恢复，否则可能挂起（M5 也强制了这一点）。
            if (Control.CoreWebView2 is not null && IsSuspended)
                await ResumeAsync();
        }
        catch { /* 忽略 */ }

        // WPF WebView2 是一个 HwndHost：Dispose() 会销毁宿主窗口，从而关闭
        // CoreWebView2Controller 并 teardown 本标签的 renderer 进程。
        try { Control.Dispose(); }
        catch { /* 忽略 */ }
    }

    // --- 挂起/恢复（M5）：冻结后台标签的 renderer 以释放内存 ---

    /// <summary>挂起本标签的 renderer。可重复调用；已挂起则为 no-op。</summary>
    public async Task TrySuspendAsync()
    {
        if (!_initialized || IsSuspended)
            return;

        try { await Core.TrySuspendAsync(); }
        catch { /* 并非总能挂起 —— 忽略 */ }

        IsSuspended = Core.IsSuspended;
        _vm.State = IsSuspended ? TabState.Suspended : TabState.Loaded;
    }

    /// <summary>唤醒一个已挂起的标签，使其重新可交互。</summary>
    public Task ResumeAsync()
    {
        if (!_initialized || !IsSuspended)
            return Task.CompletedTask;

        try { Core.Resume(); }
        catch { /* 忽略 */ }

        IsSuspended = Core.IsSuspended;
        _vm.State = IsSuspended ? TabState.Suspended : TabState.Loaded;
        return Task.CompletedTask;
    }
}
