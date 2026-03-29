namespace Abo.Core.Models;

/// <summary>
/// Represents an active web session token.
/// </summary>
public class WebSession
{
    /// <summary>
    /// The unique session token (256-bit random string).
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// The username of the authenticated user.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// When the session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the session expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The IP address of the client (for security tracking).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// The user agent string of the client.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Checks if the session is expired.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

/// <summary>
/// Container for all active web sessions stored in JSON.
/// </summary>
public class SessionStore
{
    /// <summary>
    /// List of all active web sessions.
    /// </summary>
    public List<WebSession> Sessions { get; set; } = new();
}
