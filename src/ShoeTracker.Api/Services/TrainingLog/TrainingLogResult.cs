namespace ShoeTracker.Api.Services;

/// <summary>
/// The outcome of a <see cref="TrainingLog"/> mutation. Endpoints are adapters: they map these
/// to HTTP without re-deciding any rule.
/// </summary>
public abstract record TrainingLogResult
{
    private TrainingLogResult() { }

    /// <summary>The change was made; <see cref="Value"/> is the entity as it now stands.</summary>
    public sealed record Ok<T>(T Value) : TrainingLogResult;

    /// <summary>The change was made and there is nothing to return.</summary>
    public sealed record Done : TrainingLogResult
    {
        public static readonly Done Instance = new();
    }

    /// <summary>
    /// No such run or shoe for this user. Another user's data gives the same answer, so its
    /// existence isn't revealed (404, never 403).
    /// </summary>
    public sealed record NotFound : TrainingLogResult
    {
        public static readonly NotFound Instance = new();
    }

    /// <summary>The run belongs to Strava's record and can't be changed here.</summary>
    public sealed record ReadOnly(string Message) : TrainingLogResult;

    /// <summary>The request fails validation: one field, one message.</summary>
    public sealed record Invalid(string Field, string Message) : TrainingLogResult;
}
