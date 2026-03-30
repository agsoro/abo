namespace Abo.Pm.Models;

public class LoginRequest
{
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
}

public class InitPasswordRequest
{
    public string Username { get; init; } = string.Empty;
}

public class UserInfoResponse
{
    public string Username { get; init; } = string.Empty;
    public bool IsAuthenticated { get; init; }
}

public class ErrorResponse
{
    public string Error { get; init; } = string.Empty;
}