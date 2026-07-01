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

    static AppPaths()
    {
        AppDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBrowser");

        WebView2UserDataFolder = Path.Combine(AppDataRoot, "WebView2");

        Directory.CreateDirectory(WebView2UserDataFolder);
    }
}
