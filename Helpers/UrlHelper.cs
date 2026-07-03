using System.Globalization;

namespace WebBrowser.Helpers;

/// <summary>
/// 将用户在地址栏输入的内容转换为可导航的 URL：
/// 绝对 http(s)/about URI 直接放行，裸域名补 https://，其余一律作为网页搜索。
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

        // 已是带已知 scheme 的完整 URI。
        if (Uri.TryCreate(text, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp ||
             absolute.Scheme == Uri.UriSchemeHttps ||
             absolute.Scheme == "about" ||
             absolute.Scheme == "edge"))
        {
            return absolute.ToString();
        }

        // "localhost:5000" 或 "example.com/path" —— 看起来是主机名，而非搜索词。
        var noSpaces = !text.Contains(' ');
        var looksLikeDomain = noSpaces && text.Contains('.') && !text.EndsWith('.');
        if (looksLikeDomain)
            return "https://" + text;

        // 回退到网页搜索。
        return SearchEngine + Uri.EscapeDataString(text);
    }
}
