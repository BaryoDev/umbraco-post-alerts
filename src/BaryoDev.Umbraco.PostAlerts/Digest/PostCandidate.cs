namespace BaryoDev.Umbraco.PostAlerts.Digest;

/// <summary>
/// A published post, ready to put in an email or a push. Url and CoverUrl are absolute. DateUtc is the date
/// property when set, otherwise the publish date.
/// </summary>
public sealed record PostCandidate(Guid Key, string Title, string? Summary, string? Url, string? CoverUrl, DateTime DateUtc);

public interface IPostSource
{
    /// <summary>Every published post of the configured types.</summary>
    IReadOnlyList<PostCandidate> GetAll();
    PostCandidate? Get(Guid key);
    IReadOnlyList<Guid> GetAllKeys();
}

public static class DigestSelection
{
    /// <summary>Posts dated inside the period, newest first, at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<PostCandidate> Select(IEnumerable<PostCandidate> posts, DigestPeriod period, int max) =>
        posts
            .Where(p => !string.IsNullOrWhiteSpace(p.Url) && p.DateUtc >= period.StartUtc && p.DateUtc < period.EndUtc)
            .OrderByDescending(p => p.DateUtc)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, max))
            .ToList();

    /// <summary>The newest posts regardless of date, for a test email.</summary>
    public static IReadOnlyList<PostCandidate> Latest(IEnumerable<PostCandidate> posts, int count) =>
        posts
            .Where(p => !string.IsNullOrWhiteSpace(p.Url))
            .OrderByDescending(p => p.DateUtc)
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, count))
            .ToList();
}
