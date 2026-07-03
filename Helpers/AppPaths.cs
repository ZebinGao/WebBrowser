using System.IO;

namespace WebBrowser.Helpers;

/// <summary>
/// 集中管理应用所需的文件系统路径。WebView2 的 user-data 目录放在
/// %LOCALAPPDATA%\WebBrowser 下，使 profile/缓存跨构建保留，且按用户隔离。
/// </summary>
public static class AppPaths
{
    public static string AppDataRoot { get; }

    /// <summary>共享的 WebView2 user-data 目录。一个目录 =&gt; 所有标签共享同一个 browser 进程。</summary>
    public static string WebView2UserDataFolder { get; }

    /// <summary>持久化的浏览历史（JSON，System.Text.Json）。</summary>
    public static string HistoryPath { get; }

    /// <summary>持久化的书签（JSON，System.Text.Json）。</summary>
    public static string BookmarksPath { get; }

    static AppPaths()
    {
        AppDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBrowser");

        WebView2UserDataFolder = Path.Combine(AppDataRoot, "WebView2");

        HistoryPath = Path.Combine(AppDataRoot, "history.json");
        BookmarksPath = Path.Combine(AppDataRoot, "bookmarks.json");

        Directory.CreateDirectory(WebView2UserDataFolder);
    }
}
