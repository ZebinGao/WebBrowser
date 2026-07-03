using Microsoft.Web.WebView2.Core;
using WebBrowser.Helpers;

namespace WebBrowser.Services;

/// <summary>
/// 持有全应用唯一共享的 <see cref="CoreWebView2Environment"/>。所有标签都调用 <see cref="GetEnvironmentAsync"/>
/// 复用同一 browser/GPU/network 进程树（renderer 共享取决于 site isolation）。对同一 user-data folder
/// 创建多个 environment 会引发进程抖动（WebView2Feedback #3378），故创建由 semaphore 守卫。
/// </summary>
public sealed class WebViewEnvironmentService : IDisposable
{
    private CoreWebView2Environment? _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>共享 browser 进程意外退出时触发 —— 此时每个标签的 CoreWebView2 都已死亡。</summary>
    public event EventHandler? EnvironmentLost;

    /// <summary>WebView2 运行时进程集合变化时触发（renderer 生成或退出）。</summary>
    public event EventHandler? ProcessCountChanged;

    public IReadOnlyList<CoreWebView2ProcessInfo> ProcessInfos =>
        _environment?.GetProcessInfos() ?? Array.Empty<CoreWebView2ProcessInfo>();

    /// <summary>
    /// 返回缓存的共享 environment，首次使用时创建。必须在 UI 线程调用。
    /// 并发的首次调用经 <see cref="_gate"/> 串行化，确保只构建一个 environment。
    /// </summary>
    public async Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_environment is not null)
            return _environment;

        await _gate.WaitAsync();
        try
        {
            if (_environment is not null)
                return _environment;

            // browserExecutableFolder = null => 使用已安装的 Evergreen 运行时。
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.WebView2UserDataFolder,
                options: new CoreWebView2EnvironmentOptions());

            environment.BrowserProcessExited += OnBrowserProcessExited;
            environment.ProcessInfosChanged += OnProcessInfosChanged;

            _environment = environment;
            return environment;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnBrowserProcessExited(object? sender, CoreWebView2BrowserProcessExitedEventArgs e)
    {
        // 支撑共享 environment 的 browser 进程已消失；每个 CoreWebView2 都已失效。
        _environment = null;
        EnvironmentLost?.Invoke(this, EventArgs.Empty);
    }

    private void OnProcessInfosChanged(object? sender, object e)
        => ProcessCountChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gate.Dispose();
    }
}
