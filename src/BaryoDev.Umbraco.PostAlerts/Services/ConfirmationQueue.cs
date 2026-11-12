using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BaryoDev.Umbraco.PostAlerts.Services;

/// <summary>
/// Hands confirmation emails to a background sender. Subscribe answers at the same speed for a new, a pending
/// and a confirmed address, because it never waits on the mail server.
/// </summary>
public interface IConfirmationQueue
{
    void Enqueue(OutgoingMail mail);
}

public sealed class ConfirmationQueue : IConfirmationQueue
{
    private readonly Channel<OutgoingMail> _channel = Channel.CreateUnbounded<OutgoingMail>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(OutgoingMail mail) => _channel.Writer.TryWrite(mail);

    public IAsyncEnumerable<OutgoingMail> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>Sends queued confirmation emails one by one. A failure is logged and the next mail goes on.</summary>
public sealed class ConfirmationSender(ConfirmationQueue queue, IAlertMailer mailer, ILogger<ConfirmationSender> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (OutgoingMail mail in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await mailer.SendAsync(mail, "PostAlertsConfirm", stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The visitor already got the normal answer. The log says why no email came.
                    logger.LogWarning(e, "Could not send the confirmation email.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
