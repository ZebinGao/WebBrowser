namespace WebBrowser.Models;

/// <summary>自定义下载管理器状态机的状态（自 M3 起使用）。</summary>
public enum DownloadState
{
    NotStarted,
    InProgress,
    Paused,
    Completed,
    Cancelled,
    Interrupted,
}
