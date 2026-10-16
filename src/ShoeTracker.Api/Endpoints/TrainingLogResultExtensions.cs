using ShoeTracker.Api.Services;

namespace ShoeTracker.Api.Endpoints;

/// <summary>
/// The adapter half of the TrainingLog seam: one shared mapping of rule outcomes to HTTP
/// (NotFound → 404, ReadOnly → 409, Invalid → 400). Each endpoint supplies only its success shape.
/// </summary>
internal static class TrainingLogResultExtensions
{
    public static async Task<IResult> ToResultAsync<T>(this TrainingLogResult result, Func<T, Task<IResult>> ok) =>
        result is TrainingLogResult.Ok<T> okResult ? await ok(okResult.Value) : ToErrorResult(result);

    public static IResult ToResult<T>(this TrainingLogResult result, Func<T, IResult> ok) =>
        result is TrainingLogResult.Ok<T> okResult ? ok(okResult.Value) : ToErrorResult(result);

    public static IResult ToResult(this TrainingLogResult result, IResult done) =>
        result is TrainingLogResult.Done ? done : ToErrorResult(result);

    private static IResult ToErrorResult(TrainingLogResult result) => result switch
    {
        TrainingLogResult.NotFound => Results.NotFound(),
        TrainingLogResult.ReadOnly readOnly => Results.Problem(readOnly.Message, statusCode: StatusCodes.Status409Conflict),
        TrainingLogResult.Invalid invalid => Results.ValidationProblem(new Dictionary<string, string[]> { [invalid.Field] = [invalid.Message] }),
        _ => throw new InvalidOperationException($"Unhandled TrainingLog outcome {result.GetType().Name}.")
    };
}
