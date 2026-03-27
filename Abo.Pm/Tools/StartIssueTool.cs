using System.Text.Json;
using System.Text.RegularExpressions;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Abo.Integrations.GitHub;
using Abo.Tools;
using Microsoft.Extensions.Configuration;

namespace Abo.Tools;

public class StartIssueTool : IAboTool
{
    private readonly IConfiguration _config;

    public StartIssueTool(IConfiguration config)
    {
        _config = config;
    }

    public string Name => "start_issue";
    public string Description => "Starts a new issue instance. This creates an issue in the environment's configured tracker.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            issueId = new { type = "string", description = "The unique numeric or alphanumeric ID for the instantiated issue (used as an internal reference)." },
            title = new { type = "string", description = "A concise title for the issue." },
            type = new { type = "string", description = "The type of the ticket (e.g., bug, feature, doc)." },
            info = new { type = "string", description = "The markdown content describing the goals, context, and initial parameters of the issue." },
            parentId = new { type = "string", description = "Optional. The ID of the parent issue if this is a subissue." },
            environmentName = new { type = "string", description = "The name of the environment to supply for the issue (e.g. 'thingsboard'). Execute get_environments to find valid names." }
        },
        required = new[] { "issueId", "title", "type", "info", "environmentName" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        StartIssueArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<StartIssueArgs>(argumentsJson, jsOptions);
        }
        catch
        {
            return "Failed to parse arguments.";
        }

        if (args == null || string.IsNullOrWhiteSpace(args.IssueId) || string.IsNullOrWhiteSpace(args.Type) || string.IsNullOrWhiteSpace(args.EnvironmentName))
            return "Invalid arguments provided. Ensure issueId, type, and environmentName are not empty.";

        var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "environments.json");
        ConnectorEnvironment? targetEnv = null;
        if (File.Exists(environmentsFile))
        {
            var envJson = await File.ReadAllTextAsync(environmentsFile);
            var envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envJson, jsOptions);
            targetEnv = envs?.FirstOrDefault(e => e.Name.Equals(args.EnvironmentName, StringComparison.OrdinalIgnoreCase));
            if (targetEnv == null)
            {
                return $"Error: Environment '{args.EnvironmentName}' not found. Please use get_environments to see available environments.";
            }
        }

        if (targetEnv == null) return "Error: Failed to load target environment config.";

        try
        {
            var dummyIssue = new IssueRecord { Type = args.Type, Status = "open" };
            var initialStepInfo = Abo.Core.WorkflowEngine.GetStepInfo(dummyIssue);
            if (initialStepInfo == null)
            {
                return $"Error: The initial step 'open' is not recognized by the WorkflowEngine.";
            }
            var requiredRole = initialStepInfo.Role?.RoleId;

            // Setup Issue Tracker
            IIssueTrackerConnector? tracker = null;
            if (targetEnv.IssueTracker != null)
            {
                if (targetEnv.IssueTracker.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                {
                    var token = _config["Integrations:GitHub:Token"];
                    tracker = new GitHubIssueTrackerConnector(targetEnv.IssueTracker, token, targetEnv.Name);
                }
                else if (targetEnv.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                {
                    tracker = new FileSystemIssueTrackerConnector(targetEnv.Name);
                }
            }

            if (tracker == null)
            {
                return $"Error: Environment '{args.EnvironmentName}' has no configured IssueTracker.";
            }

            // Write info.md equivalent
            var bodyArgs = $"**Internal Reference ID:** {args.IssueId}\n**Type:** {args.Type}\n**Parent:** {args.ParentId ?? "None"}\n**Environment:** {args.EnvironmentName}\n\n## Context\n{args.Info}";

            // Map State to Labels
            var labels = new List<string>
            {
                $"step: open",
                $"ref: {args.IssueId}"
            };

            if (!string.IsNullOrWhiteSpace(args.ParentId)) labels.Add($"parent: {args.ParentId}");

            var issue = await tracker.CreateIssueAsync(args.Title, bodyArgs, args.Type, "", labels.ToArray());

            return $"Successfully started issue '{args.IssueId}' ({args.Title}). Tracking via Issue ID: {issue.Id}. State initialized at step 'open'.";
        }
        catch (Exception ex)
        {
            return $"Error starting issue: {ex.Message}";
        }
    }

    private class StartIssueArgs
    {
        public string IssueId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Info { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string EnvironmentName { get; set; } = string.Empty;
    }
}
