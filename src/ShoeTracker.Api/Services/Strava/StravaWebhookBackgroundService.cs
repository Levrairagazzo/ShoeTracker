using ShoeTracker.Api.Services.Geocoding;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>
/// Works through queued webhook events one at a time, so two events for the same activity
/// can't race each other.
/// </summary>
public class StravaWebhookBackgroundService(
    IServiceScopeFactory scopes,
    StravaWebhookQueue queue,
    PlaceNameSignal placeNames,
    ILogger<StravaWebhookBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var webhookEvent in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var runsChanged = await scope.ServiceProvider.GetRequiredService<StravaWebhookHandler>().HandleAsync(webhookEvent, stoppingToken);
                    if (runsChanged) placeNames.Notify();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A missed event isn't lost for good: the next import picks the run up.
                    logger.LogWarning(ex, "Handling Strava webhook event ({ObjectType} {ObjectId}, {AspectType}) failed.",
                        webhookEvent.ObjectType, webhookEvent.ObjectId, webhookEvent.AspectType);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
