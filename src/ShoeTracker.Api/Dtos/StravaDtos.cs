using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShoeTracker.Api.Dtos;

/// <param name="Available">Whether the server has Strava API credentials configured at all.</param>
/// <param name="ImportedRuns">How many of the user's runs came from Strava.</param>
/// <param name="UnassignedRuns">How many of the user's runs aren't assigned to a shoe.</param>
public record StravaStatusResponse(bool Available, bool Connected, string? AthleteName, int ImportedRuns, int UnassignedRuns);

public record StravaImportResponse(int Imported);

/// <summary>A webhook event as Strava posts it.</summary>
/// <param name="ObjectType">"activity" or "athlete".</param>
/// <param name="ObjectId">The activity's id, or the athlete's for athlete events.</param>
/// <param name="AspectType">"create", "update" or "delete".</param>
/// <param name="OwnerId">The athlete the event is about.</param>
/// <param name="Updates">Changed fields on updates, e.g. <c>"authorized": "false"</c> when the athlete revokes the app.</param>
public record StravaWebhookEvent(
    [property: JsonPropertyName("object_type")] string ObjectType,
    [property: JsonPropertyName("object_id")] long ObjectId,
    [property: JsonPropertyName("aspect_type")] string AspectType,
    [property: JsonPropertyName("owner_id")] long OwnerId,
    [property: JsonPropertyName("subscription_id")] long SubscriptionId,
    [property: JsonPropertyName("event_time")] long EventTime,
    [property: JsonPropertyName("updates")] Dictionary<string, JsonElement>? Updates = null);
