using System.Security.Cryptography;
using System.Text.Json;
using Abo.Core.Models;
using Microsoft.Extensions.Logging;

namespace Abo.Core.Services;

/// <summary>
/// Service for managing web session lifecycle with JSON persistence.
/// </summary>
public class SessionStoreService : ISessionStore
{
    private readonly string _sessionsPath;
    private readonly ILogger<SessionStoreService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Default session expiration in hours.
    /// </summary>
    public int SessionExpirationHours { get; set; } = 24;

    /// <summary>
    /// Creates a new SessionStoreService instance.
    /// </summary>
    /// <param name="dataDirectory">Directory where sessions.json will be stored.</param>
    /// <param name="logger">Logger instance.</param>
    public SessionStoreService(string dataDirectory, ILogger<SessionStoreService> logger)
    {
        _sessionsPath = Path.Combine(dataDirectory, "sessions.json");
        _logger = logger;

        EnsureDataDirectoryExists(dataDirectory);
    }

    private static void EnsureDataDirectoryExists(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    /// <inheritdoc />
    public async Task SaveSessionAsync(WebSession session)
    {
        var store = await LoadSessionsAsync();

        // Update existing session or add new one
        var existingIndex = store.Sessions.FindIndex(s => s.Token == session.Token);
        if (existingIndex >= 0)
        {
            store.Sessions[existingIndex] = session;
        }
        else
        {
            store.Sessions.Add(session);
        }

        await SaveSessionsAsync(store);
    }

    /// <inheritdoc />
    public async Task<WebSession?> GetSessionAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var store = await LoadSessionsAsync();
        var session = store.Sessions.FirstOrDefault(s => s.Token == token);

        // Check if expired and clean up
        if (session != null && session.IsExpired)
        {
            store.Sessions.Remove(session);
            await SaveSessionsAsync(store);
            _logger.LogDebug("Expired session removed for user: {Username}", session.Username);
            return null;
        }

        return session;
    }

    /// <inheritdoc />
    public async Task DeleteSessionAsync(string token)
    {
        var store = await LoadSessionsAsync();
        var session = store.Sessions.FirstOrDefault(s => s.Token == token);

        if (session != null)
        {
            store.Sessions.Remove(session);
            await SaveSessionsAsync(store);
        }
    }

    /// <inheritdoc />
    public async Task CleanupExpiredSessionsAsync()
    {
        var store = await LoadSessionsAsync();
        var expired = store.Sessions.Where(s => s.IsExpired).ToList();

        if (expired.Count > 0)
        {
            foreach (var session in expired)
            {
                store.Sessions.Remove(session);
            }

            await SaveSessionsAsync(store);
            _logger.LogInformation("Cleaned up {Count} expired sessions", expired.Count);
        }
    }

    /// <summary>
    /// Generates a secure session token.
    /// </summary>
    public static string GenerateSessionToken()
    {
        var bytes = new byte[32]; // 256 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    #region Private Helper Methods

    private async Task<Models.SessionStore> LoadSessionsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_sessionsPath))
            {
                return new Models.SessionStore();
            }

            var json = await File.ReadAllTextAsync(_sessionsPath);
            return JsonSerializer.Deserialize<Models.SessionStore>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new Models.SessionStore();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveSessionsAsync(Models.SessionStore store)
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(_sessionsPath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    #endregion
}
