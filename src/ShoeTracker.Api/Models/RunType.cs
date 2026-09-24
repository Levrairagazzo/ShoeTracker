using System.Text.Json.Serialization;

namespace ShoeTracker.Api.Models;

/// <summary>What kind of run it was, as far as Strava can tell (manual runs have none).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RunType>))]
public enum RunType
{
    /// <summary>Strava's plain "Run": usually road, but a trail run recorded as "Run" lands here too.</summary>
    Run,
    Trail,
    Treadmill
}
