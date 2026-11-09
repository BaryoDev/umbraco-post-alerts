using System.Text;
using System.Text.Json;
using BaryoDev.Umbraco.PostAlerts.Services;

namespace BaryoDev.Umbraco.PostAlerts.Push;

/// <summary>The JSON the service worker reads. Push services cap a payload near 4 KB, so the text is cut.</summary>
public static class PushPayload
{
    public const int MaxTitle = 100;
    public const int MaxBody = 160;
    public const int MaxBytes = 3500;

    public static string Build(Guid key, string title, string? summary, string url)
    {
        string cleanTitle = TextCleaner.Cut(TextCleaner.Summary(title, 1000), MaxTitle);
        string body = TextCleaner.Summary(summary, MaxBody);
        string json = Serialize(key, cleanTitle, body, url);
        // A very long URL could still push the payload over. Drop the body before dropping the message.
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
        {
            json = Serialize(key, cleanTitle, "", url);
        }
        return json;
    }

    private static string Serialize(Guid key, string title, string body, string url) =>
        JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["title"] = title,
            ["body"] = body,
            ["url"] = url,
            ["tag"] = "post-" + key.ToString("N"),
        });
}
