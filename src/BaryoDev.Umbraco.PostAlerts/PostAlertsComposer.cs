using BaryoDev.Umbraco.PostAlerts.Data;
using BaryoDev.Umbraco.PostAlerts.Digest;
using BaryoDev.Umbraco.PostAlerts.Push;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace BaryoDev.Umbraco.PostAlerts;

public sealed class PostAlertsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<PostAlertsOptions>().BindConfiguration(PostAlertsOptions.Section);
        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddSingleton<ISubscriberRepository, SubscriberRepository>();
        builder.Services.AddSingleton<IPushSubscriptionRepository, PushSubscriptionRepository>();
        builder.Services.AddSingleton<IDigestRunRepository, DigestRunRepository>();
        builder.Services.AddSingleton<IPushedPostRepository, PushedPostRepository>();

        builder.Services.AddSingleton<RateLimiter>();
        builder.Services.AddSingleton<TokenService>();
        builder.Services.AddSingleton<UmbracoAlertMailer>();
        builder.Services.TryAddSingleton<IAlertMailer, SmtpAlertMailer>();
        builder.Services.AddSingleton<ConfirmationQueue>();
        builder.Services.AddSingleton<IConfirmationQueue>(sp => sp.GetRequiredService<ConfirmationQueue>());
        builder.Services.AddHostedService<ConfirmationSender>();
        builder.Services.AddSingleton<SubscriptionService>();

        builder.Services.TryAddSingleton<IProtectionCheck, PublicAccessProtectionCheck>();
        builder.Services.AddSingleton<IPostSource, UmbracoPostSource>();
        builder.Services.AddSingleton<DigestService>();
        builder.Services.AddRecurringBackgroundJob<DigestJob>();

        builder.Services.AddHttpClient(WebPushTransport.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        builder.Services.TryAddSingleton<IPushTransport, WebPushTransport>();
        builder.Services.AddSingleton<PushSender>();
        builder.Services.AddSingleton<PushQueue>();
        builder.Services.AddSingleton<IPushQueue>(sp => sp.GetRequiredService<PushQueue>());
        builder.Services.AddHostedService<PushDispatchService>();

        builder.AddNotificationHandler<ContentPublishedNotification, PostPublishedHandler>();
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, PushSeeder>();
    }
}
