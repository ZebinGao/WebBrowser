using System.Globalization;

namespace WebBrowser.Helpers;

/// <summary>
/// Turns whatever the user typed in the address bar into a navigable URL:
/// absolute http(s)/about URIs pass through, bare domains get https://, everything else is a web search.
/// </summary>
public static class UrlHelper
{
    private const string SearchEngine = "https://www.bing.com/search?q=";

    public static string Normalize(string? input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
            return "about:blank";

        if (string.Equals(text, "about:blank", StringComparison.OrdinalIgnoreCase))
            return "about:blank";

        // Already a full URI with a known scheme.
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp ||
             absolute.Scheme == Uri.UriSchemeHttps ||
             absolute.Scheme == "about" ||
             absolute.Scheme == "edge"))
        {
            return absolute.ToString();
        }

        // "localhost:5000" or "example.com/path" — looks like a host, not a search.
        var noSpaces = !text.Contains(' ');
        var looksLikeDomain = noSpaces && text.Contains('.') && !text.EndsWith('.');
        if (looksLikeDomain)
            return "https://" + text;

        // Fall back to a web search.
        return SearchEngine + Uri.EscapeDataString(text);
    }
}
