namespace WebBrowser.Models;

/// <summary>States for the custom download manager state machine (used from M3 onward).</summary>
public enum DownloadState
{
    NotStarted,
    InProgress,
    Paused,
    Completed,
    Cancelled,
    Interrupted,
}
