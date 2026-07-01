namespace WebBrowser.Models;

/// <summary>High-level lifecycle state of a single tab. Drives UI affordances (spinner, suspend badge, error page).</summary>
public enum TabState
{
    Loading,
    Loaded,
    Error,
    Suspended,
}
