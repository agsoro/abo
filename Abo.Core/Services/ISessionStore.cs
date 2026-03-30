using Abo.Core.Models;

namespace Abo.Core.Services;

/// <summary>
/// Interface for managing web session lifecycle.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Saves or updates a session.
    /// </summary>
    Task SaveSessionAsync(WebSession session);

    /// <summary>
    /// Gets a session by token if it exists and is not expired.
    /// </summary>
    Task<WebSession?> GetSessionAsync(string token);

    /// <summary>
    /// Gets all sessions (including expired ones).
    /// </summary>
    Task<IReadOnlyList<WebSession>> GetAllSessionsAsync();

    /// <summary>
    /// Removes a session by token.
    /// </summary>
    Task DeleteSessionAsync(string token);

    /// <summary>
    /// Removes all expired sessions.
    /// </summary>
    Task CleanupExpiredSessionsAsync();
}
