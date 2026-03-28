using Abo.Integrations.GitHub;

namespace Abo.Core.Connectors;

/// <summary>
/// Represents a configured working environment for an agent.
/// This defines the storage locations, operating system specifics, and associated integrations (like Issue Trackers)
/// that an agent has access to when processing a issue.
/// </summary>
public class ConnectorEnvironment
{
    /// <summary>
    /// The unique name of the environment (e.g., "abo", "production").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The type of the environment, such as "local" or "remote".
    /// </summary>
    public string Type { get; set; } = "local";

    /// <summary>
    /// The operating system the environment runs on, such as "win" or "linux".
    /// </summary>
    public string Os { get; set; } = "win";

    /// <summary>
    /// The primary technology stack of the environment's projects.
    /// Valid values: "dotnet", "python", "node", "mixed".
    /// Required — used to filter which runtime tools are available to agents.
    /// </summary>
    public string Technology { get; set; } = string.Empty;

    /// <summary>
    /// The root directory path for the environment where workspace operations (file reading/writing, git, dotnet) take place.
    /// </summary>
    public string Dir { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit wiki directory path. If specified, overrides connector-specific defaults.
    /// For GitHub wiki: use this as the clone directory directly.
    /// For filesystem wiki: use this as the wiki root directory.
    /// </summary>
    public string? WikiDir { get; set; }

    /// <summary>
    /// IssueTracker configured for this environment (e.g., github, jira).
    /// </summary>
    public IssueTrackerConfig? IssueTracker { get; set; }
    /// <summary>
    /// Wiki configured for this environment (e.g., filesystem, xpectolive).
    /// </summary>
    public WikiConfig? Wiki { get; set; }
}

/// <summary>
/// Configuration details for linking an environment to a Wiki backend.
/// </summary>
public class WikiConfig
{
    /// <summary>
    /// The type of the wiki backend system (e.g., "filesystem", "xpectolive").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The root path or identifier for the wiki.
    /// For a filesystem wiki, this acts as a subpath within the environment's directory (e.g., "\doc").
    /// For an external wiki like XpectoLive, this acts as the Space ID.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for the Issue Tracker linked to an environment.
/// </summary>
public class IssueTrackerConfig
{
    public string Type { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public Dictionary<string, string> ProjectTitles { get; set; } = new();
}
