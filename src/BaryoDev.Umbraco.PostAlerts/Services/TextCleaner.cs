using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BaryoDev.Umbraco.PostAlerts.Services;

public static partial class TextCleaner
{
    /// <summary>A summary with thousands of unclosed tags can make the patterns slow. Past this, the text is dropped.</summary>
    private const int MatchTimeoutMs = 250;

    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase, MatchTimeoutMs)]
    private static partial Regex Blocks();

    [GeneratedRegex("<[^>]*>", RegexOptions.None, MatchTimeoutMs)]
    private static partial Regex Tags();

    [GeneratedRegex("\\s+", RegexOptions.None, MatchTimeoutMs)]
    private static partial Regex Spaces();

    /// <summary>Strips markup, decodes entities, collapses white space and cuts to <paramref name="max"/> characters at a word edge.</summary>
    public static string Summary(string? html, int max)
    {
        if (string.IsNullOrWhiteSpace(html) || max < 1)
        {
            return string.Empty;
        }
        try
        {
            string text = Blocks().Replace(html, " ");
            text = Tags().Replace(text.Replace("</p>", " ").Replace("<br>", " ").Replace("<br/>", " ").Replace("<br />", " "), "");
            text = WebUtility.HtmlDecode(text);
            text = Spaces().Replace(StripControl(text), " ").Trim();
            return Cut(text, max);
        }
        catch (RegexMatchTimeoutException)
        {
            return string.Empty;
        }
    }

    /// <summary>Cuts at a word edge and adds three dots when text was dropped. Does not split a surrogate pair.</summary>
    public static string Cut(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }
        int end = max;
        if (char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }
        int space = text.LastIndexOf(' ', end - 1, end);
        if (space > max / 2)
        {
            end = space;
        }
        return text[..end].TrimEnd(' ', ',', ';', ':', '.', '-') + "...";
    }

    private static string StripControl(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (!char.IsControl(c) || c is '\n' or '\t')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
