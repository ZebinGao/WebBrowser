namespace WebBrowser.Models;

/// <summary>
/// 一条用户保存的书签。纯粹的、可序列化的 POCO（无 MVVM 基类），以利干净的 JSON 持久化。
/// 扁平列表 —— 不含文件夹，刻意如此。托管于 <see cref="Services.BookmarksService.Items"/>。
/// </summary>
public sealed class Bookmark
{
    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }
}
