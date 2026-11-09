using System.Globalization;
using System.Net;
using System.Text;
using BaryoDev.Umbraco.PostAlerts.Services;

namespace BaryoDev.Umbraco.PostAlerts.Digest;

public sealed record DigestEmailModel(
    string SiteName,
    string SiteUrl,
    IReadOnlyList<PostCandidate> Posts,
    string UnsubscribeUrl,
    TimeZoneInfo Zone,
    int SummaryMaxLength,
    bool IsTest = false);

/// <summary>Headers holds List-Unsubscribe and List-Unsubscribe-Post (RFC 8058), for a mailer that can set headers.</summary>
public sealed record RenderedDigest(string Subject, string Html, string Text, IReadOnlyDictionary<string, string> Headers);

/// <summary>Builds the digest as table-based HTML with inline styles, plus a plain-text version.</summary>
public static class DigestRenderer
{
    private const string Ink = "#1c2321";
    private const string Soft = "#4d5552";
    private const string Line = "#e5e5e5";
    private const string Font = "Arial,Helvetica,sans-serif";

    public static RenderedDigest Render(DigestEmailModel m)
    {
        var posts = m.Posts.Where(p => SafeUrl(p.Url) is not null).ToList();
        string subject = Subject(m, posts);
        string unsubscribe = SafeUrl(m.UnsubscribeUrl) ?? "";

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (unsubscribe.Length > 0)
        {
            headers["List-Unsubscribe"] = "<" + unsubscribe + ">";
            headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
        }
        return new RenderedDigest(subject, Html(m, posts, subject, unsubscribe), Text(m, posts, unsubscribe), headers);
    }

    internal static string Subject(DigestEmailModel m, IReadOnlyList<PostCandidate> posts)
    {
        string core = posts.Count == 1
            ? "New post: " + posts[0].Title
            : posts.Count.ToString(CultureInfo.InvariantCulture) + " new posts on " + m.SiteName;
        core = new string(core.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return m.IsTest ? "[Test] " + core : core;
    }

    private static string Html(DigestEmailModel m, IReadOnlyList<PostCandidate> posts, string subject, string unsubscribe)
    {
        var sb = new StringBuilder(4096);
        string site = Enc(m.SiteName);
        string preheader = posts.Count > 0 ? Enc(posts[0].Title) : site;

        sb.Append("<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<meta name=\"color-scheme\" content=\"light\"><meta name=\"supported-color-schemes\" content=\"light\">")
          .Append("<title>").Append(Enc(subject)).Append("</title></head>\n")
          .Append("<body style=\"margin:0;padding:0;background-color:#f4f4f4;\">\n")
          .Append("<div style=\"display:none;max-height:0;overflow:hidden;font-size:1px;line-height:1px;color:#f4f4f4;\">").Append(preheader).Append("</div>\n")
          .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"#f4f4f4\" style=\"background-color:#f4f4f4;\"><tr><td align=\"center\" style=\"padding:24px 12px;\">\n")
          .Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" bgcolor=\"#ffffff\" style=\"width:100%;max-width:600px;background-color:#ffffff;\">\n");

        string siteLink = SafeUrl(m.SiteUrl) ?? "";
        sb.Append("<tr><td style=\"padding:24px 24px 4px 24px;font-family:").Append(Font).Append(";font-size:14px;line-height:20px;color:").Append(Soft).Append(";\">");
        sb.Append(siteLink.Length > 0
            ? $"<a href=\"{Attr(siteLink)}\" style=\"color:{Soft};text-decoration:none;\">{site}</a>"
            : site);
        sb.Append("</td></tr>\n");

        foreach (PostCandidate p in posts)
        {
            string url = Attr(SafeUrl(p.Url)!);
            string? cover = SafeUrl(p.CoverUrl);
            string summary = TextCleaner.Summary(p.Summary, m.SummaryMaxLength);
            string date = TimeZoneInfo.ConvertTimeFromUtc(p.DateUtc, m.Zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);

            if (cover is not null)
            {
                sb.Append("<tr><td style=\"padding:20px 24px 0 24px;\"><a href=\"").Append(url).Append("\">")
                  .Append("<img src=\"").Append(Attr(cover)).Append("\" alt=\"\" width=\"552\" style=\"display:block;width:100%;max-width:552px;height:auto;border:0;outline:none;\">")
                  .Append("</a></td></tr>\n");
            }
            sb.Append("<tr><td style=\"padding:").Append(cover is null ? "20px" : "12px").Append(" 24px 0 24px;font-family:").Append(Font).Append(";\">")
              .Append("<a href=\"").Append(url).Append("\" style=\"font-size:20px;line-height:26px;font-weight:bold;color:").Append(Ink).Append(";text-decoration:none;\">")
              .Append(Enc(p.Title)).Append("</a></td></tr>\n");
            sb.Append("<tr><td style=\"padding:4px 24px 0 24px;font-family:").Append(Font).Append(";font-size:13px;line-height:18px;color:").Append(Soft).Append(";\">")
              .Append(Enc(date)).Append("</td></tr>\n");
            if (summary.Length > 0)
            {
                sb.Append("<tr><td style=\"padding:8px 24px 0 24px;font-family:").Append(Font).Append(";font-size:16px;line-height:24px;color:").Append(Ink).Append(";\">")
                  .Append(Enc(summary)).Append("</td></tr>\n");
            }
            sb.Append("<tr><td style=\"padding:8px 24px 20px 24px;font-family:").Append(Font).Append(";font-size:15px;line-height:22px;border-bottom:1px solid ").Append(Line).Append(";\">")
              .Append("<a href=\"").Append(url).Append("\" style=\"color:").Append(Ink).Append(";text-decoration:underline;\">Read the post</a></td></tr>\n");
        }

        sb.Append("<tr><td style=\"padding:20px 24px 28px 24px;font-family:").Append(Font).Append(";font-size:13px;line-height:19px;color:").Append(Soft).Append(";\">")
          .Append("You get this email because you subscribed to new post alerts on ").Append(site).Append(".");
        if (unsubscribe.Length > 0)
        {
            sb.Append(" <a href=\"").Append(Attr(unsubscribe)).Append("\" style=\"color:").Append(Soft).Append(";text-decoration:underline;\">Unsubscribe</a>.");
        }
        sb.Append("</td></tr>\n</table>\n</td></tr></table>\n</body></html>\n");
        return sb.ToString();
    }

    private static string Text(DigestEmailModel m, IReadOnlyList<PostCandidate> posts, string unsubscribe)
    {
        var sb = new StringBuilder();
        sb.Append(m.SiteName).Append(": ").Append(posts.Count.ToString(CultureInfo.InvariantCulture))
          .Append(posts.Count == 1 ? " new post" : " new posts").Append("\n\n");
        int n = 1;
        foreach (PostCandidate p in posts)
        {
            string date = TimeZoneInfo.ConvertTimeFromUtc(p.DateUtc, m.Zone).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            string summary = TextCleaner.Summary(p.Summary, m.SummaryMaxLength);
            sb.Append(n++.ToString(CultureInfo.InvariantCulture)).Append(". ").Append(p.Title).Append(" (").Append(date).Append(")\n");
            if (summary.Length > 0)
            {
                sb.Append("   ").Append(summary).Append('\n');
            }
            sb.Append("   ").Append(SafeUrl(p.Url)).Append("\n\n");
        }
        sb.Append("--\n");
        if (unsubscribe.Length > 0)
        {
            sb.Append("Unsubscribe: ").Append(unsubscribe).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Only absolute http and https links get into the email. Anything else is left out.</summary>
    private static string? SafeUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? u) && u.Scheme is "http" or "https" ? u.AbsoluteUri : null;

    private static string Enc(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string Attr(string value) => WebUtility.HtmlEncode(value);
}
