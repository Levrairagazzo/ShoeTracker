namespace ShoeTracker.Api.Models;

/// <summary>
/// A user's link to their Strava account. Tokens are stored encrypted with ASP.NET Core
/// Data Protection; read them through <c>StravaTokenStore</c>, never directly.
/// </summary>
public class StravaConnection
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public long AthleteId { get; set; }

    public string AthleteName { get; set; } = string.Empty;

    public string EncryptedAccessToken { get; set; } = string.Empty;

    public string EncryptedRefreshToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The scopes the user granted, as Strava reports them (comma-separated).</summary>
    public string Scope { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAt { get; set; }

    /// <summary>
    /// When the first import after connecting finished, i.e. the user's history is in. Until then
    /// webhook events are ignored, since a run they added would make the import skip the history.
    /// </summary>
    public DateTimeOffset? HistoryImportedAt { get; set; }
}
