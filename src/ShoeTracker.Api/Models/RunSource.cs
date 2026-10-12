using System.Text.Json.Serialization;

namespace ShoeTracker.Api.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RunSource>))]
public enum RunSource
{
    Manual,
    Strava
}
