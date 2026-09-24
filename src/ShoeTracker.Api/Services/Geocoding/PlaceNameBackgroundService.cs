using Microsoft.Extensions.Options;

namespace ShoeTracker.Api.Services.Geocoding;

/// <summary>Wakes <see cref="PlaceNameBackgroundService"/> early, e.g. right after a Strava import.</summary>
public class PlaceNameSignal
{
    private readonly SemaphoreSlim _signal = new(0);

    public void Notify()
    {
        // Coalesce: one pending wake-up is enough however many imports finish.
        if (_signal.CurrentCount == 0) _signal.Release();
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Resolves place names in the background: once at startup, whenever <see cref="PlaceNameSignal"/>
/// fires, and every few minutes as a fallback (e.g. to retry after a failed lookup).
/// </summary>
public class PlaceNameBackgroundService(
    IServiceScopeFactory scopes,
    PlaceNameSignal signal,
    IOptions<GeocodingOptions> options,
    ILogger<PlaceNameBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var resolved = await scope.ServiceProvider.GetRequiredService<PlaceNameResolver>().ResolvePendingAsync(stoppingToken);
                if (resolved > 0) logger.LogInformation("Resolved {Count} run locations.", resolved);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Resolving run locations failed.");
            }

            try
            {
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
