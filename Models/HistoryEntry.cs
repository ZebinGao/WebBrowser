namespace WebBrowser.Models;

/// <summary>
/// One visited page in the browsing history. Plain serializable POCO — deliberately no MVVM base
/// class so it serializes cleanly to JSON via System.Text.Json. Hosted in
/// <see cref="Services.HistoryService.Entries"/>; the panel DataTemplate binds to it directly.
/// </summary>
public sealed class HistoryEntry
{
    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    /// <summary>When this URL was last visited. Repeated visits update this and bump <see cref="VisitCount"/>.</summary>
    public DateTimeOffset LastVisited { get; set; }

    /// <summary>How many times this URL has been visited.</summary>
    public int VisitCount { get; set; }
}
