using System.IO;

namespace WebBrowser.Helpers;

/// <summary>
/// Centralized filesystem paths for the app. The WebView2 user-data folder is kept under
/// %LOCALAPPDATA%\WebBrowser so profiles/cache survive across builds and stay user-scoped.
/// </summary>
public static class AppPaths
{
    public static string AppDataRoot { get; }

    /// <summary>Shared WebView2 user-data folder. One folder =&gt; one shared browser process for all tabs.</summary>
    public static string WebView2UserDataFolder { get; }

    /// <summary>Persisted browsing history (JSON, System.Text.Json).</summary>
    public static string HistoryPath { get; }

    /// <summary>Persisted bookmarks (JSON, System.Text.Json).</summary>
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
