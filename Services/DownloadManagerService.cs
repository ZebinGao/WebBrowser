using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using WebBrowser.ViewModels;

namespace WebBrowser.Services;

/// <summary>
/// 持有自定义下载管理器。集中处理每个标签的 <see cref="CoreWebView2.DownloadStarting"/>
/// （接线只在一处），抑制 WebView2 默认下载条，在用户 Downloads 目录下选定唯一路径，
/// 并维护实时下载集合。
/// </summary>
public sealed class DownloadManagerService
{
    // FOLDERID_Downloads —— 真实的按用户 Downloads 目录（不一定总是 %USERPROFILE%\Downloads）。
    private static readonly Guid FolderIdDownloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>下载默认落到此处。通过 shell KnownFolder API 解析，缺失时创建。</summary>
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

    /// <summary>重试下载时触发；宿主应让某标签导航到该 URL 以重新触发下载。</summary>
    public event EventHandler<Uri>? RetryRequested;

    /// <summary>挂接某标签的 CoreWebView2，使它的下载在此被拦截。</summary>
    public void Register(CoreWebView2 core) => core.DownloadStarting += OnDownloadStarting;

    /// <summary>拆卸时解挂，避免（可能已死的）Core 因该 handler 而不被回收。</summary>
    public void Unregister(CoreWebView2 core)
    {
        try { core.DownloadStarting -= OnDownloadStarting; }
        catch { /* Core 可能已不存在 */ }
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // 抑制内置下载条；我们自己渲染 UI。
        e.Handled = true;

        CoreWebView2DownloadOperation operation = e.DownloadOperation;

        string fileName = !string.IsNullOrWhiteSpace(e.ResultFilePath)
            ? Path.GetFileName(e.ResultFilePath)
            : SafeFileNameFromUri(operation.Uri);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.EndsWith(':'))
            fileName = "download";

        string fullPath = ResolveUniquePath(Path.Combine(DefaultDownloadFolder, fileName));

        // ResultFilePath 只能在此 handler 内同步设置（#2890）。
        e.ResultFilePath = fullPath;

        var item = new DownloadItemViewModel(operation, fullPath);
        item.RetryRequested += uri => RetryRequested?.Invoke(this, uri);

        // DownloadStarting 可能在非 UI 线程触发；集合变更需转发到 UI 线程。
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

    /// <summary>若文件已存在，在扩展名前追加 " (1)"、" (2)"… 直到 9999。</summary>
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
