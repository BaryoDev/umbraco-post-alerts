using System.Text.Json.Serialization;
using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Push;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.PostAlerts.Api;

public sealed class PushKeys
{
    [JsonPropertyName("p256dh")] public string? P256dh { get; set; }
    [JsonPropertyName("auth")] public string? Auth { get; set; }
}

/// <summary>The body of a browser PushSubscription. Both the flat form and the <c>keys</c> form of toJSON() are accepted.</summary>
public sealed class PushSubscriptionRequest
{
    [JsonPropertyName("endpoint")] public string? Endpoint { get; set; }
    [JsonPropertyName("p256dh")] public string? P256dh { get; set; }
    [JsonPropertyName("auth")] public string? Auth { get; set; }
    [JsonPropertyName("keys")] public PushKeys? Keys { get; set; }
}

public sealed class PushEndpointRequest
{
    [JsonPropertyName("endpoint")] public string? Endpoint { get; set; }
}

/// <summary>Public browser push endpoints. JSON only, no cookies read, nothing cached.</summary>
[ApiController]
[Route("baryo-post-alerts")]
public sealed class PushController(
    IPushSubscriptionRepository subscriptions,
    RateLimiter limiter,
    IOptionsMonitor<PostAlertsOptions> options,
    TimeProvider time) : ControllerBase
{
    [HttpGet("push/key")]
    public IActionResult PublicKey()
    {
        Response.Headers.CacheControl = "no-store";
        PostAlertsOptions o = options.CurrentValue;
        return PostAlertsOptionsCheck.Check(o).PushReady ? Ok(new { publicKey = o.VapidPublicKey.Trim() }) : NotFound();
    }

    [HttpPost("push/subscription")]
    [Consumes("application/json")]
    public IActionResult Subscribe([FromBody] PushSubscriptionRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        PostAlertsOptions o = options.CurrentValue;
        if (!PostAlertsOptionsCheck.Check(o).PushReady)
        {
            return NotFound();
        }
        if (!limiter.TryAcquire("push:" + ClientKey.From(HttpContext.Connection.RemoteIpAddress), o.RateLimitMaxRequests, TimeSpan.FromMinutes(o.RateLimitWindowMinutes)))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests);
        }

        string? p256dh = Normalize(request.P256dh ?? request.Keys?.P256dh, 65, 0x04);
        string? auth = Normalize(request.Auth ?? request.Keys?.Auth, 16, null);
        if (p256dh is null || auth is null || !PushEndpointPolicy.IsAllowed(request.Endpoint, o.PushEndpointHosts))
        {
            return BadRequest(new { error = "The subscription is not valid." });
        }

        string endpoint = request.Endpoint!.Trim();
        string hash = TokenService.Hash(endpoint);
        if (subscriptions.Count() >= Math.Max(1, o.PushMaxSubscriptions))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        subscriptions.Upsert(new PushSubscriptionRow
        {
            Id = Guid.NewGuid(),
            EndpointHash = hash,
            Endpoint = endpoint,
            P256dh = p256dh,
            Auth = auth,
            CreatedUtc = time.GetUtcNow().UtcDateTime,
        });
        return NoContent();
    }

    /// <summary>Removes a subscription. The endpoint is a secret the browser holds, so knowing it is enough. Always 204, so it says nothing about what is stored.</summary>
    [HttpDelete("push/subscription")]
    [Consumes("application/json")]
    public IActionResult Unsubscribe([FromBody] PushEndpointRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        if (!string.IsNullOrWhiteSpace(request.Endpoint) && request.Endpoint.Length <= PushSubscriptionRow.EndpointLength)
        {
            subscriptions.DeleteByEndpointHash(TokenService.Hash(request.Endpoint.Trim()));
        }
        return NoContent();
    }

    /// <summary>
    /// A standalone service worker for sites that have none. It is the push handlers plus install and activate steps.
    /// Sites with their own worker import /App_Plugins/BaryoDev.PostAlerts/push-sw.js instead.
    /// </summary>
    [HttpGet("sw.js")]
    public IActionResult ServiceWorker()
    {
        using Stream? stream = typeof(PushController).Assembly.GetManifestResourceStream("push-sw.js");
        if (stream is null)
        {
            return NotFound();
        }
        using var reader = new StreamReader(stream);
        string script = reader.ReadToEnd() +
            "\nself.addEventListener('install', function () { self.skipWaiting(); });\n" +
            "self.addEventListener('activate', function (event) { event.waitUntil(self.clients.claim()); });\n";
        Response.Headers["Service-Worker-Allowed"] = "/";
        Response.Headers.CacheControl = "no-cache";
        return Content(script, "text/javascript");
    }

    /// <summary>Strips padding and checks the decoded length. Returns the unpadded base64url form, or null.</summary>
    internal static string? Normalize(string? value, int length, byte? firstByte)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
        {
            return null;
        }
        string trimmed = value.Trim().TrimEnd('=').Replace('+', '-').Replace('/', '_');
        try
        {
            byte[] bytes = WebEncoders.Base64UrlDecode(trimmed);
            return bytes.Length == length && (firstByte is null || bytes[0] == firstByte) ? trimmed : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
