namespace WebBrowser.Models;

/// <summary>
/// One user-saved bookmark. Plain serializable POCO (no MVVM base) for clean JSON persistence.
/// Flat list — no folders, by design. Hosted in <see cref="Services.BookmarksService.Items"/>.
/// </summary>
public sealed class Bookmark
{
    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }
}
