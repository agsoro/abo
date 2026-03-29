namespace Abo.Core.Models;

// Request DTOs
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class InitPasswordRequest
{
    public string Username { get; set; } = string.Empty;
    public string? MattermostUsername { get; set; }  // Where to send the password
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
}

// Response DTOs
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class UserInfoResponse
{
    public string Username { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
}

public class InitPasswordResponse
{
    public string Message { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
}
