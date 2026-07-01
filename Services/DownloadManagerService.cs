using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WebBrowser.ViewModels;

namespace WebBrowser.Services;

/// <summary>
/// Owns the custom download manager. Centralizes <see cref="CoreWebView2.DownloadStarting"/> handling
/// for every tab (so wiring lives in one place), suppresses the default WebView2 download bar, picks a
/// unique path under the user's Downloads folder, and maintains the live collection of downloads.
/// </summary>
public sealed class DownloadManagerService
{
    // FOLDERID_Downloads — the real per-user Downloads folder (not always %USERPROFILE%\Downloads).
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>Downloads land here by default. Resolved via the shell KnownFolder API, created if missing.</summary>
    public static string DefaultDownloadFolder { get; } = ResolveDownloadsFolder();

    private static string ResolveDownloadsFolder()
    {
        string folder;
        try
        {
            folder = SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero, out string? known) == 0 && !string.IsNullOrEmpty(known)
                ? known
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        catch
        {
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        Directory.CreateDirectory(folder);
        return folder;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out string? pszPath);

    public ObservableCollection<DownloadItemViewModel> Items { get; } = new();

    /// <summary>Raised when a download is retried; the host should navigate a tab to the URL to re-trigger it.</summary>
    public event EventHandler<Uri>? RetryRequested;

    /// <summary>Hook a tab's CoreWebView2 so its downloads are intercepted here.</summary>
    public void Register(CoreWebView2 core) => core.DownloadStarting += OnDownloadStarting;

    /// <summary>Unhook on teardown so the (possibly dead) Core isn't kept alive by this handler.</summary>
    public void Unregister(CoreWebView2 core)
    {
        try { core.DownloadStarting -= OnDownloadStarting; }
        catch { /* Core may already be gone */ }
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // Suppress the built-in download bar; we render our own UI.
        e.Handled = true;

        CoreWebView2DownloadOperation operation = e.DownloadOperation;

        string fileName = !string.IsNullOrWhiteSpace(e.ResultFilePath)
            ? Path.GetFileName(e.ResultFilePath)
            : SafeFileNameFromUri(operation.Uri);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.EndsWith(':'))
            fileName = "download";

        string fullPath = ResolveUniquePath(Path.Combine(DefaultDownloadFolder, fileName));

        // ResultFilePath can only be set synchronously in this handler (#2890).
        e.ResultFilePath = fullPath;

        var item = new DownloadItemViewModel(operation, fullPath);
        item.RetryRequested += uri => RetryRequested?.Invoke(this, uri);

        // DownloadStarting can fire on a non-UI thread; marshal collection mutation to the UI thread.
        Application.Current.Dispatcher.BeginInvoke(() => Items.Insert(0, item));
    }

    private static string SafeFileNameFromUri(string? uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u))
        {
            string last = u.Segments.Length > 0 ? u.Segments[^1].TrimEnd('/') : string.Empty;
            return Uri.UnescapeDataString(last);
        }
        return "download";
    }

    /// <summary>If a file already exists, append " (1)", " (2)", ... up to 9999 before the extension.</summary>
    private static string ResolveUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string? dir = Path.GetDirectoryName(path);
        string file = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        for (int i = 1; i < 9999; i++)
        {
            string candidate = Path.Combine(dir ?? string.Empty, $"{file} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return path;
    }
}
