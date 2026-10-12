using System.Threading.Channels;
using ShoeTracker.Api.Dtos;

namespace ShoeTracker.Api.Services.Strava;

/// <summary>
/// Hands webhook events from the endpoint to <see cref="StravaWebhookBackgroundService"/>, since
/// Strava wants an answer within 2 seconds and handling an event means calling Strava back.
/// </summary>
public class StravaWebhookQueue
{
    /// <summary>Bounded because the webhook endpoint is public.</summary>
    public const int Capacity = 1000;

    private readonly Channel<StravaWebhookEvent> _channel = Channel.CreateBounded<StravaWebhookEvent>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    /// <returns>False if the queue is full and the event was dropped.</returns>
    public bool TryEnqueue(StravaWebhookEvent webhookEvent) => _channel.Writer.TryWrite(webhookEvent);

    public IAsyncEnumerable<StravaWebhookEvent> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
