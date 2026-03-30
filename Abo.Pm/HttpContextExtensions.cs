using Abo.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Abo.Pm;

/// <summary>
/// Extension methods for HttpContext to extract and validate authentication from Bearer tokens.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// The Bearer prefix used in Authorization headers.
    /// </summary>
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Attempts to extract and validate a Bearer token from the Authorization header.
    /// Returns the authentication status and username if valid.
    /// </summary>
    /// <param name="context">The HTTP context to extract the authorization from.</param>
    /// <param name="sessionStore">The session store to validate the token against.</param>
    /// <returns>
    /// A tuple containing:
    /// - IsAuthenticated: true if a valid, non-expired session was found
    /// - Username: the authenticated username if valid, null otherwise
    /// </returns>
    /// <remarks>
    /// This method does NOT automatically reject requests - callers decide how to handle
    /// unauthenticated requests based on the returned tuple values.
    /// 
    /// Return semantics:
    /// - Missing Authorization header → (false, null)
    /// - Invalid format (not Bearer token) → (false, null)
    /// - Token not found → (false, null)
    /// - Expired token → (false, null)
    /// - Valid token → (true, username)
    /// </remarks>
    public static async Task<(bool IsAuthenticated, string? Username)> TryGetAuthenticatedUserAsync(
        this HttpContext context,
        ISessionStore sessionStore)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessionStore);

        var token = ExtractBearerToken(context);
        
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, null);
        }

        var session = await sessionStore.GetSessionAsync(token);
        
        if (session == null)
        {
            return (false, null);
        }

        return (true, session.Username);
    }

    /// <summary>
    /// Gets the Authorization header value from the HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The Authorization header value, or null if not present.</returns>
    private static string? GetAuthorizationHeader(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return authHeader.FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Extracts the Bearer token from the Authorization header.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The token value without the "Bearer " prefix, or null if not present/invalid.</returns>
    private static string? ExtractBearerToken(HttpContext context)
    {
        var authHeader = GetAuthorizationHeader(context);

        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return null;
        }

        if (!authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader.Substring(BearerPrefix.Length).Trim();

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
