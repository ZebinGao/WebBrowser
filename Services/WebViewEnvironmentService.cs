using Microsoft.Web.WebView2.Core;
using WebBrowser.Helpers;

namespace WebBrowser.Services;

/// <summary>
/// Owns the single shared <see cref="CoreWebView2Environment"/> for the whole app.
/// All tabs call <see cref="GetEnvironmentAsync"/> and reuse the same browser/GPU/network
/// process tree (renderer sharing depends on site isolation). Creating more than one
/// environment against the same user-data folder causes process churn (WebView2Feedback #3378),
/// so creation is guarded by a semaphore.
/// </summary>
public sealed class WebViewEnvironmentService : IDisposable
{
    private CoreWebView2Environment? _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    /// <summary>Raised when the shared browser process exits unexpectedly — every tab's CoreWebView2 is now dead.</summary>
    public event EventHandler? EnvironmentLost;

    /// <summary>Raised when the set of WebView2 runtime processes changes (a renderer spawned or exited).</summary>
    public event EventHandler? ProcessCountChanged;

    public IReadOnlyList<CoreWebView2ProcessInfo> ProcessInfos =>
        _environment?.GetProcessInfos() ?? Array.Empty<CoreWebView2ProcessInfo>();

    /// <summary>
    /// Returns the cached shared environment, creating it on first use. Must be invoked on the UI thread.
    /// Concurrent first-time callers are serialized via <see cref="_gate"/> so only one environment is built.
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

            // browserExecutableFolder = null => use the installed Evergreen runtime.
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
        // The browser process backing the shared environment is gone; every CoreWebView2 is now invalid.
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
