using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Media;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace BaryoDev.Umbraco.PostAlerts.Digest;

/// <summary>
/// Reads posts from the published cache. It opens its own Umbraco context so it works in a background job.
/// Sites with several languages use the default language.
/// </summary>
internal sealed class UmbracoPostSource(
    IUmbracoContextFactory contextFactory,
    IPublishedContentCache contentCache,
    IDocumentNavigationQueryService navigation,
    IPublishedContentStatusFilteringService statusFilter,
    IPublishedUrlProvider urls,
    IImageUrlGenerator imageUrls,
    IContentService contentService,
    IProtectionCheck protection,
    IDefaultCultureAccessor defaultCulture,
    IOptionsMonitor<PostAlertsOptions> options,
    ILogger<UmbracoPostSource> logger) : IPostSource
{
    public IReadOnlyList<PostCandidate> GetAll()
    {
        PostAlertsOptions o = options.CurrentValue;
        using var _ = contextFactory.EnsureUmbracoContext();
        string? culture = Culture();
        var posts = new List<PostCandidate>();
        foreach (IPublishedContent c in Published(o, culture))
        {
            try
            {
                posts.Add(Map(c, o, culture));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Skipped post {Key}: could not read it.", c.Key);
            }
        }
        return posts;
    }

    public PostCandidate? Get(Guid key)
    {
        PostAlertsOptions o = options.CurrentValue;
        using var _ = contextFactory.EnsureUmbracoContext();
        string? culture = Culture();
        IPublishedContent? c = contentCache.GetById(key);
        return c is not null && IsPost(c, o) && !protection.IsExcluded(c.Path) && c.IsPublished(culture) ? Map(c, o, culture) : null;
    }

    public IReadOnlyList<Guid> GetAllKeys()
    {
        PostAlertsOptions o = options.CurrentValue;
        using var _ = contextFactory.EnsureUmbracoContext();
        return Published(o, Culture()).Select(c => c.Key).ToList();
    }

    private string? Culture() => string.IsNullOrEmpty(defaultCulture.DefaultCulture) ? null : defaultCulture.DefaultCulture;

    private static bool IsPost(IPublishedContent c, PostAlertsOptions o) =>
        o.DocumentTypes.Contains(c.ContentType.Alias, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<IPublishedContent> Published(PostAlertsOptions o, string? culture)
    {
        if (!navigation.TryGetRootKeys(out IEnumerable<Guid> roots))
        {
            return [];
        }
        var keys = new List<Guid>();
        foreach (Guid root in roots)
        {
            if (navigation.TryGetDescendantsKeysOrSelfKeys(root, out IEnumerable<Guid> found))
            {
                keys.AddRange(found);
            }
        }
        return statusFilter.FilterAvailable(keys, culture).Where(c => IsPost(c, o) && !protection.IsExcluded(c.Path) && c.IsPublished(culture)).ToList();
    }

    private PostCandidate Map(IPublishedContent c, PostAlertsOptions o, string? culture)
    {
        PostAlertsOptionsCheck.TryZone(o.TimeZone, out TimeZoneInfo zone);

        string? summary = Text(c, o.SummaryAlias, culture);
        string? relative = urls.GetUrl(c, UrlMode.Relative, culture);
        string? url = string.IsNullOrEmpty(relative) || relative == "#" ? null : AbsoluteUrl.Make(o.SiteBaseUrl, relative);

        DateTime? date = string.IsNullOrWhiteSpace(o.DateAlias)
            ? null
            : PostDates.FromPropertyValue(c.GetProperty(o.DateAlias)?.GetValue(culture), zone);
        if (date is null)
        {
            // Umbraco keeps the publish date on the draft model, not on the published item.
            DateTime? published = contentService.GetById(c.Key)?.PublishDate;
            date = published is { } p ? PostDates.ToUtc(DateTime.SpecifyKind(p, p.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : p.Kind), TimeZoneInfo.Utc) : c.CreateDate.ToUniversalTime();
        }

        return new PostCandidate(c.Key, c.Name, summary, url, Cover(c, o, culture), date.Value);
    }

    private static string? Text(IPublishedContent c, string alias, string? culture) =>
        string.IsNullOrWhiteSpace(alias) ? null : c.GetProperty(alias)?.GetValue(culture)?.ToString();

    private string? Cover(IPublishedContent c, PostAlertsOptions o, string? culture)
    {
        foreach (string alias in o.CoverAlias ?? [])
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }
            string? source = MediaSource(c.GetProperty(alias)?.GetValue(culture), culture);
            if (string.IsNullOrEmpty(source))
            {
                continue;
            }
            return AbsoluteUrl.Make(o.SiteBaseUrl, Resize(source, o));
        }
        return null;
    }

    private string? MediaSource(object? value, string? culture)
    {
        IPublishedContent? media = value switch
        {
            MediaWithCrops one => one.Content,
            IEnumerable<MediaWithCrops> many => many.FirstOrDefault()?.Content,
            IPublishedContent item => item,
            IEnumerable<IPublishedContent> items => items.FirstOrDefault(),
            _ => null,
        };
        if (media is not null)
        {
            return urls.GetMediaUrl(media, UrlMode.Relative, culture);
        }
        return value is ImageCropperValue cropper ? cropper.Src : null;
    }

    private string Resize(string url, PostAlertsOptions o)
    {
        string path = url.Split('?', '#')[0];
        string ext = Path.GetExtension(path).TrimStart('.');
        if (ext.Length == 0 || !imageUrls.SupportedImageFileTypes.Contains(ext, StringComparer.OrdinalIgnoreCase) || o.CoverWidth < 1)
        {
            return url;
        }
        var options = new ImageUrlGenerationOptions(url) { Width = o.CoverWidth };
        if (o.CoverHeight > 0)
        {
            options.Height = o.CoverHeight;
            options.ImageCropMode = ImageCropMode.Crop;
        }
        return imageUrls.GetImageUrl(options) ?? url;
    }
}
