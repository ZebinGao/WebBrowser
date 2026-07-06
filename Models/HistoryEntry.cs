namespace WebBrowser.Models;

/// <summary>
/// 浏览历史中访问过的一个页面。纯粹的、可序列化的 POCO —— 刻意不使用 MVVM 基类，
/// 以便通过 System.Text.Json 干净地序列化为 JSON。托管于 <see cref="Services.HistoryService.Entries"/>；
/// 面板的 DataTemplate 直接绑定到它。
/// </summary>
public sealed class HistoryEntry
{
    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>该 URL 最近一次被访问的时间。重复访问会更新此项并增加 <see cref="VisitCount"/>。</summary>
    public DateTimeOffset LastVisited { get; set; }

    /// <summary>该 URL 被访问过的次数。</summary>
    public int VisitCount { get; set; }
}
