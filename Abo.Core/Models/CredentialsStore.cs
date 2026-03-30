namespace Abo.Core.Models;

/// <summary>
/// Represents stored user credentials with hashed password.
/// </summary>
public class UserCredential
{
    /// <summary>
    /// The unique username for login.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// BCrypt hashed password.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// When the credential was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the password was last changed.
    /// </summary>
    public DateTime? LastPasswordChange { get; set; }

    /// <summary>
    /// When the user last logged in successfully.
    /// </summary>
    public DateTime? LastLogin { get; set; }

    /// <summary>
    /// Whether the user account is active. Inactive users cannot authenticate.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether the password has been changed from the initial generated one.
    /// </summary>
    public bool PasswordChanged { get; set; } = false;

    /// <summary>
    /// Optional: Associated Mattermost user ID for notifications.
    /// </summary>
    public string? MattermostUserId { get; set; }

    /// <summary>
    /// User roles for authorization.
    /// </summary>
    public List<string> Roles { get; set; } = new();
}

/// <summary>
/// Container for all user credentials stored in JSON.
/// </summary>
public class CredentialsStore
{
    /// <summary>
    /// List of all user credentials.
    /// </summary>
    public List<UserCredential> Users { get; set; } = new();
}
