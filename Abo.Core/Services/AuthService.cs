using System.Security.Cryptography;
using System.Text.Json;
using Abo.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abo.Core.Services;

/// <summary>
/// Options for authentication configuration.
/// </summary>
public class AuthOptions
{
    /// <summary>
    /// Default session expiration time in hours.
    /// </summary>
    public int SessionExpirationHours { get; set; } = 24;

    /// <summary>
    /// Default password length for auto-generated passwords.
    /// </summary>
    public int DefaultPasswordLength { get; set; } = 16;
}

/// <summary>
/// Service for handling user authentication, credential management, and session handling.
/// </summary>
public class AuthService
{
    private readonly string _credentialsPath;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;
    private readonly ISessionStore _sessionStore;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// BCrypt work factor for password hashing. Higher = more secure but slower.
    /// </summary>
    private const int BcryptWorkFactor = 12;

    public AuthService(
        string dataDirectory,
        IOptions<AuthOptions> options,
        ILogger<AuthService> logger,
        ISessionStore sessionStore)
    {
        _credentialsPath = Path.Combine(dataDirectory, "credentials.json");
        _options = options.Value;
        _logger = logger;
        _sessionStore = sessionStore;

        EnsureDataDirectoryExists(dataDirectory);
    }

    private static void EnsureDataDirectoryExists(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            Directory.CreateDirectory(dataDirectory);
        }
    }

    /// <summary>
    /// Initializes the auth system, including creating the CEO user if it doesn't exist.
    /// </summary>
    /// <param name="mattermostClient">Optional MattermostClient for sending initial passwords.</param>
    /// <param name="ceoUsername">Optional CEO username to send the initial password to.</param>
    public async Task InitializeAsync(
        Abo.Integrations.Mattermost.MattermostClient? mattermostClient = null,
        string? ceoUsername = null)
    {
        var store = await LoadCredentialsAsync();

        // Check if CEO user exists, if not create it
        var ceo = store.Users.FirstOrDefault(u =>
            u.Username.Equals("ceo", StringComparison.OrdinalIgnoreCase));

        if (ceo == null)
        {
            _logger.LogInformation("CEO user not found, creating initial account...");

            // Generate secure random password
            var password = GenerateSecurePassword(_options.DefaultPasswordLength);
            var hash = HashPassword(password);

            ceo = new UserCredential
            {
                Username = "ceo",
                PasswordHash = hash,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                PasswordChanged = false,
                Roles = new List<string> { "admin", "user" }
            };

            store.Users.Add(ceo);
            await SaveCredentialsAsync(store);

            _logger.LogInformation("CEO user created successfully");

            // Send password via Mattermost if configured
            if (mattermostClient != null && !string.IsNullOrWhiteSpace(ceoUsername))
            {
                try
                {
                    var message = $"Hello! Your ABO web interface account has been created.\n\n" +
                                  $"**Username:** `ceo`\n" +
                                  $"**Initial Password:** `{password}`\n\n" +
                                  $"Please change your password after first login.\n" +
                                  $"Access the web interface at your ABO server URL.";

                    var sent = await mattermostClient.SendDirectMessageAsync(ceoUsername, message);
                    if (sent)
                    {
                        _logger.LogInformation("CEO initial password sent via Mattermost to {CeoUsername}", ceoUsername);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to send CEO initial password via Mattermost to {CeoUsername}", ceoUsername);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending CEO initial password via Mattermost");
                }
            }
            else
            {
                _logger.LogWarning("Mattermost not configured, CEO initial password not sent. Check logs for generated password.");
            }
        }
        else
        {
            _logger.LogInformation("CEO user already exists, skipping initialization");
        }
    }

    /// <summary>
    /// Validates username and password, returns a session token if valid.
    /// Inactive users cannot authenticate.
    /// </summary>
    public async Task<(bool Success, string? Token, string? Error)> LoginAsync(
        string username,
        string password,
        string? ipAddress = null,
        string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (false, null, "Username and password are required");
        }

        var store = await LoadCredentialsAsync();
        var user = store.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            _logger.LogWarning("Login attempt for non-existent user: {Username}", username);
            return (false, null, "Invalid username or password");
        }

        // Check if user account is active - inactive users cannot authenticate
        if (!user.IsActive)
        {
            _logger.LogWarning("Login attempt for inactive user: {Username}", username);
            return (false, null, "Account is inactive");
        }

        // Verify password
        if (!VerifyPassword(password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for user: {Username}", username);
            return (false, null, "Invalid username or password");
        }

        // Update last login time
        user.LastLogin = DateTime.UtcNow;
        await SaveCredentialsAsync(store);

        // Create session
        var token = SessionStoreService.GenerateSessionToken();
        var session = new WebSession
        {
            Token = token,
            Username = user.Username,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(_options.SessionExpirationHours),
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        await _sessionStore.SaveSessionAsync(session);

        _logger.LogInformation("User {Username} logged in successfully", username);
        return (true, token, null);
    }

    /// <summary>
    /// Invalidates a session token (logout).
    /// </summary>
    public async Task<bool> LogoutAsync(string token)
    {
        var session = await _sessionStore.GetSessionAsync(token);

        if (session == null)
        {
            return false;
        }

        await _sessionStore.DeleteSessionAsync(token);

        _logger.LogInformation("User {Username} logged out", session.Username);
        return true;
    }

    /// <summary>
    /// Validates a session token and returns the associated username if valid.
    /// </summary>
    public async Task<(bool Valid, string? Username)> ValidateSessionAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, null);
        }

        var session = await _sessionStore.GetSessionAsync(token);

        if (session == null)
        {
            return (false, null);
        }

        return (true, session.Username);
    }

    /// <summary>
    /// Gets user information for an authenticated session.
    /// </summary>
    public async Task<UserCredential?> GetUserAsync(string username)
    {
        var store = await LoadCredentialsAsync();
        return store.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Creates a new user with an auto-generated password.
    /// </summary>
    public async Task<(bool Success, string? Password, string? Error)> CreateUserAsync(
        string username,
        List<string>? roles = null,
        Abo.Integrations.Mattermost.MattermostClient? mattermostClient = null,
        string? targetUsername = null)
    {
        // Validate username
        if (string.IsNullOrWhiteSpace(username))
        {
            return (false, null, "Username is required");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-zA-Z][a-zA-Z0-9_\-]{2,19}$"))
        {
            return (false, null, "Username must be 3-20 characters, start with a letter, and contain only letters, numbers, underscores, or hyphens");
        }

        var store = await LoadCredentialsAsync();

        // Check for duplicate
        if (store.Users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, null, $"User '{username}' already exists");
        }

        // Generate password and create user
        var password = GenerateSecurePassword(_options.DefaultPasswordLength);
        var hash = HashPassword(password);

        var user = new UserCredential
        {
            Username = username,
            PasswordHash = hash,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            PasswordChanged = false,
            Roles = roles ?? new List<string> { "user" }
        };

        store.Users.Add(user);
        await SaveCredentialsAsync(store);

        _logger.LogInformation("User {Username} created successfully", username);

        // Send password via Mattermost if configured
        if (mattermostClient != null && !string.IsNullOrWhiteSpace(targetUsername))
        {
            try
            {
                var message = $"A new ABO web interface account has been created for you.\n\n" +
                              $"**Username:** `{username}`\n" +
                              $"**Initial Password:** `{password}`\n\n" +
                              $"Please change your password after first login.";

                var sent = await mattermostClient.SendDirectMessageAsync(targetUsername, message);
                if (sent)
                {
                    _logger.LogInformation("Initial password for user {Username} sent via Mattermost to {TargetUsername}",
                        username, targetUsername);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending initial password for user {Username} via Mattermost", username);
            }
        }

        return (true, password, null);
    }

    /// <summary>
    /// Changes a user's password.
    /// </summary>
    public async Task<(bool Success, string? Error)> ChangePasswordAsync(
        string username,
        string currentPassword,
        string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return (false, "New password is required");
        }

        if (newPassword.Length < 8)
        {
            return (false, "New password must be at least 8 characters long");
        }

        var store = await LoadCredentialsAsync();
        var user = store.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            return (false, "User not found");
        }

        // Verify current password
        if (!VerifyPassword(currentPassword, user.PasswordHash))
        {
            return (false, "Current password is incorrect");
        }

        // Update password
        user.PasswordHash = HashPassword(newPassword);
        user.LastPasswordChange = DateTime.UtcNow;
        user.PasswordChanged = true;

        await SaveCredentialsAsync(store);

        _logger.LogInformation("Password changed for user {Username}", username);

        // Invalidate all existing sessions for this user (they need to re-login)
        await InvalidateUserSessionsAsync(username);

        return (true, null);
    }

    /// <summary>
    /// Resets a user's password (admin function, generates new password).
    /// </summary>
    public async Task<(bool Success, string? Password, string? Error)> ResetPasswordAsync(
        string username,
        Abo.Integrations.Mattermost.MattermostClient? mattermostClient = null,
        string? targetUsername = null)
    {
        var store = await LoadCredentialsAsync();
        var user = store.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            return (false, null, "User not found");
        }

        // Generate new password
        var newPassword = GenerateSecurePassword(_options.DefaultPasswordLength);
        user.PasswordHash = HashPassword(newPassword);
        user.LastPasswordChange = DateTime.UtcNow;
        user.PasswordChanged = false; // Reset to require password change

        await SaveCredentialsAsync(store);

        _logger.LogInformation("Password reset for user {Username}", username);

        // Invalidate all sessions for this user
        await InvalidateUserSessionsAsync(username);

        // Send new password via Mattermost if configured
        if (mattermostClient != null && !string.IsNullOrWhiteSpace(targetUsername))
        {
            try
            {
                var message = $"Your ABO password has been reset by an administrator.\n\n" +
                              $"**Username:** `{username}`\n" +
                              $"**New Password:** `{newPassword}`\n\n" +
                              $"Please change your password after first login.";

                var sent = await mattermostClient.SendDirectMessageAsync(targetUsername, message);
                if (sent)
                {
                    _logger.LogInformation("Password reset notification for user {Username} sent via Mattermost", username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending password reset for user {Username} via Mattermost", username);
            }
        }

        return (true, newPassword, null);
    }

    /// <summary>
    /// Deletes a user account.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeleteUserAsync(string username)
    {
        // Prevent deleting the CEO
        if (username.Equals("ceo", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Cannot delete the CEO account");
        }

        var store = await LoadCredentialsAsync();
        var user = store.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            return (false, "User not found");
        }

        store.Users.Remove(user);
        await SaveCredentialsAsync(store);

        // Invalidate all sessions for this user
        await InvalidateUserSessionsAsync(username);

        _logger.LogInformation("User {Username} deleted", username);
        return (true, null);
    }

    /// <summary>
    /// Lists all users (without password hashes).
    /// </summary>
    public async Task<List<UserCredential>> ListUsersAsync()
    {
        var store = await LoadCredentialsAsync();
        return store.Users.Select(u => new UserCredential
        {
            Username = u.Username,
            CreatedAt = u.CreatedAt,
            LastLogin = u.LastLogin,
            LastPasswordChange = u.LastPasswordChange,
            PasswordChanged = u.PasswordChanged,
            IsActive = u.IsActive,
            MattermostUserId = u.MattermostUserId,
            Roles = u.Roles
            // PasswordHash intentionally excluded
        }).ToList();
    }

    /// <summary>
    /// Deactivates a user account. Inactive users cannot authenticate.
    /// </summary>
    public async Task<(bool Success, string? Error)> DeactivateUserAsync(string username)
    {
        // Prevent deactivating the CEO
        if (username.Equals("ceo", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Cannot deactivate the CEO account");
        }

        var store = await LoadCredentialsAsync();
        var user = store.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            return (false, "User not found");
        }

        user.IsActive = false;
        await SaveCredentialsAsync(store);

        // Invalidate all sessions for this user
        await InvalidateUserSessionsAsync(username);

        _logger.LogInformation("User {Username} deactivated and all sessions invalidated", username);
        return (true, null);
    }

    /// <summary>
    /// Activates a user account.
    /// </summary>
    public async Task<(bool Success, string? Error)> ActivateUserAsync(string username)
    {
        var store = await LoadCredentialsAsync();
        var user = store.Users.FirstOrDefault(u =>
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user == null)
        {
            return (false, "User not found");
        }

        user.IsActive = true;
        await SaveCredentialsAsync(store);

        _logger.LogInformation("User {Username} activated", username);
        return (true, null);
    }

    /// <summary>
    /// Cleans up expired sessions.
    /// </summary>
    public async Task CleanupExpiredSessionsAsync()
    {
        await _sessionStore.CleanupExpiredSessionsAsync();
    }

    #region Private Helper Methods

    /// <summary>
    /// Invalidates all sessions for a specific user.
    /// </summary>
    private async Task InvalidateUserSessionsAsync(string username)
    {
        var allSessions = await _sessionStore.GetAllSessionsAsync();
        var userSessions = allSessions.Where(s =>
            s.Username.Equals(username, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var session in userSessions)
        {
            await _sessionStore.DeleteSessionAsync(session.Token);
        }

        if (userSessions.Any())
        {
            _logger.LogInformation("Invalidated {Count} sessions for user {Username}",
                userSessions.Count, username);
        }
    }

    private async Task<CredentialsStore> LoadCredentialsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_credentialsPath))
            {
                return new CredentialsStore();
            }

            var json = await File.ReadAllTextAsync(_credentialsPath);
            return JsonSerializer.Deserialize<CredentialsStore>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new CredentialsStore();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveCredentialsAsync(CredentialsStore store)
    {
        await _lock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            await File.WriteAllTextAsync(_credentialsPath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateSecurePassword(int length)
    {
        const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowercase = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%^&*";
        const string allChars = uppercase + lowercase + digits + special;

        var bytes = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);

        // Ensure at least one of each required character type
        var password = new char[length];
        password[0] = uppercase[bytes[0] % uppercase.Length];
        password[1] = lowercase[bytes[1] % lowercase.Length];
        password[2] = digits[bytes[2] % digits.Length];
        password[3] = special[bytes[3] % special.Length];

        for (int i = 4; i < length; i++)
        {
            password[i] = allChars[bytes[i] % allChars.Length];
        }

        // Shuffle the password
        for (int i = password.Length - 1; i > 0; i--)
        {
            int j = bytes[i] % (i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }

    #endregion
}