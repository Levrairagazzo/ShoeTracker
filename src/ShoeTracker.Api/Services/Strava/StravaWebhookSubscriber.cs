using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>
/// Makes sure Strava sends webhook events to <see cref="StravaOptions.WebhookCallbackUrl"/>:
/// once the server is listening, creates the app's subscription if it has none. Does nothing
/// when no callback URL is configured (e.g. local dev, where Strava can't reach the app).
/// </summary>
public class StravaWebhookSubscriber(
    IServiceScopeFactory scopes,
    IOptions<StravaOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<StravaWebhookSubscriber> logger) : BackgroundService
{
    /// <summary>
    /// Echoed back by Strava when it validates a new subscription. Only needed for the one this
    /// process creates, so a fresh random value per process is enough.
    /// </summary>
    public string VerifyToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.IsConfigured || string.IsNullOrWhiteSpace(o.WebhookCallbackUrl)) return;

        // Strava calls the callback URL while creating the subscription, so wait until it's live.
        var started = new TaskCompletionSource();
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        {
            try
            {
                await started.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        try
        {
            await EnsureSubscriptionAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Subscribing to Strava webhook events failed; new runs only arrive via \"Import new runs\" until the next restart.");
        }
    }

    public async Task EnsureSubscriptionAsync(CancellationToken ct = default)
    {
        var callbackUrl = options.Value.WebhookCallbackUrl!;
        using var scope = scopes.CreateScope();
        var strava = scope.ServiceProvider.GetRequiredService<StravaClient>();

        var existing = await strava.GetPushSubscriptionsAsync(ct);
        if (existing.Any(s => s.CallbackUrl == callbackUrl))
        {
            logger.LogInformation("Strava webhook subscription for {CallbackUrl} is in place.", callbackUrl);
            return;
        }

        // Strava allows one subscription per app, and dev and prod share the app: never replace
        // someone else's automatically.
        if (existing.Count > 0)
        {
            logger.LogWarning("The Strava app is already subscribed with {Existing}, not {CallbackUrl}; delete that subscription to switch.",
                existing[0].CallbackUrl, callbackUrl);
            return;
        }

        var id = await strava.CreatePushSubscriptionAsync(callbackUrl, VerifyToken, ct);
        logger.LogInformation("Created Strava webhook subscription {SubscriptionId} for {CallbackUrl}.", id, callbackUrl);
    }
}
