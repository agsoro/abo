namespace Abo.Integrations.GitHub;

/// <summary>
/// Configuration details for linking an environment to a GitHub repository.
/// </summary>
public class GitHubIntegrationConfig
{
    /// <summary>
    /// The owner or organization name that hosts the repository or project.
    /// </summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>
    /// The specific repository or project name within GitHub.
    /// </summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>
    /// The base API URL for the issue tracker. Defaults to the public GitHub API.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.github.com";
}
