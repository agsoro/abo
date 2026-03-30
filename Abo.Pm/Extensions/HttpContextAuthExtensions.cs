using Abo.Core.Services;
using Microsoft.AspNetCore.Http;

namespace Abo.Pm.Extensions;

/// <summary>
/// Extension methods for HttpContext to extract and validate authentication from Bearer tokens.
/// </summary>
public static class HttpContextAuthExtensions
{
    /// <summary>
    /// The Bearer prefix used in Authorization headers.
    /// </summary>
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Extracts the Bearer token from the Authorization header.
    /// </summary>
    /// <param name="context">The HTTP context to extract the token from.</param>
    /// <returns>The token value without the "Bearer " prefix, or null if not present or invalid.</returns>
    public static string? GetBearerToken(this HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        
        if (string.IsNullOrEmpty(authHeader) || 
            !authHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader.Substring(BearerPrefix.Length).Trim();
    }

    /// <summary>
    /// Validates the Bearer token and returns the authenticated user if valid.
    /// </summary>
    /// <param name="context">The HTTP context containing the Authorization header.</param>
    /// <param name="authService">The authentication service to validate the token against.</param>
    /// <returns>
    /// A tuple containing:
    /// - IsAuthenticated: true if a valid, non-expired session was found
    /// - Username: the authenticated username if valid, null otherwise
    /// </returns>
    public static async Task<(bool IsAuthenticated, string? Username)> TryGetAuthenticatedUserAsync(
        this HttpContext context,
        AuthService authService)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authService);

        var token = context.GetBearerToken();
        
        if (string.IsNullOrEmpty(token))
        {
            return (false, null);
        }

        return await authService.ValidateSessionAsync(token);
    }
}
