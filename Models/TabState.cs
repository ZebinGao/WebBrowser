namespace WebBrowser.Models;

/// <summary>单个标签的高层生命周期状态。驱动 UI 呈现（加载圈、挂起角标、错误页）。</summary>
public enum TabState
{
    Loading,
    Loaded,
    Error,
    Suspended,
}
