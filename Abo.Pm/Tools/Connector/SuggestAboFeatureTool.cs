using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Abo.Integrations.GitHub;
using Abo.Tools;

namespace Abo.Tools.Connector;

/// <summary>
/// Allows agents to autonomously suggest new tools or capabilities for their work.
/// When invoked, this tool creates a new issue in the issue tracker with an analysis prompt
/// and optionally creates a sub-issue for implementation if the feature should be implemented.
/// </summary>
public class SuggestAboFeatureTool : IAboTool
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _config;

    public string Name => "suggest_abo_feature";
    public string Description => "Allows agents to autonomously suggest new tools or capabilities for their work. Creates an issue with an analysis prompt and optionally a sub-issue for implementation.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            name = new
            {
                type = "string",
                description = "The name of the suggested feature or capability that would help the agent complete their work."
            },
            description = new
            {
                type = "string",
                description = "The detailed description of the suggested feature or capability that would help the agent complete their work."
            }
        },
        required = new[] { "description" },
        additionalProperties = false
    };

    public SuggestAboFeatureTool(IServiceProvider serviceProvider, IConfiguration config)
    {
        _serviceProvider = serviceProvider;
        _config = config;
    }

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args == null || !args.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
            {
                return "Error: 'description' parameter is required.";
            }

            if (args == null || !args.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                return "Error: 'name' parameter is required.";
            }

            // Check if a tool with this name already exists
            if (!string.IsNullOrWhiteSpace(name))
            {
                var allTools = _serviceProvider.GetServices<IAboTool>();
                var existingTool = allTools.FirstOrDefault(t =>
                    t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals($"add_{name}", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals($"{name}_tool", StringComparison.OrdinalIgnoreCase));

                if (existingTool != null)
                {
                    return $"Error: Tool '{existingTool.Name}' already exists but is not available to your role. " +
                           "You should not request this by design. If you believe this is an error, please contact your administrator.";
                }
            }

            // Generate the issue title
            var shortDescription = description.Length > 100
                ? description.Substring(0, 97) + "..."
                : description;
            var title = $"[abo] suggest: {shortDescription}";

            // Create the analysis prompt body
            var body = $"++ analyze the suggestion of the agent to add {description} ++\n\n" +
                       "## Original Request\n" +
                       $"{description}\n\n" +
                       "## Analysis Required\n" +
                       "Please evaluate this feature suggestion based on:\n" +
                       "- **Usefulness**: How would this improve agent productivity?\n" +
                       "- **Complexity**: What is the estimated implementation effort?\n" +
                       "- **Priority**: Should this be prioritized against current work?\n" +
                       "- **Alternatives**: Are there existing tools that could achieve similar goals?";

            var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "environments.json");
            var envs = new List<ConnectorEnvironment>();
            if (File.Exists(environmentsFile))
            {
                var envJson = await File.ReadAllTextAsync(environmentsFile);
                var jsOpt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envJson, jsOpt) ?? new();
            }

            var targetEnv = envs.FirstOrDefault(e => e.Name.Equals("abo", StringComparison.OrdinalIgnoreCase) && e.IssueTracker != null)
                         ?? envs.FirstOrDefault(e => e.IssueTracker != null);

            if (targetEnv == null) return "Error: No issue tracker configured for 'abo' or any other environment.";

            IIssueTrackerConnector connector;
            if (targetEnv.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
            {
                connector = new GitHubIssueTrackerConnector(targetEnv.IssueTracker, _config["Integrations:GitHub:Token"], targetEnv.Name);
            }
            else
            {
                connector = new FileSystemIssueTrackerConnector(targetEnv.Name);
            }

            // Create the main analysis issue
            var issue = await connector.CreateIssueAsync(
                title: title,
                body: body,
                type: IssueType.Feature,
                size: "S",
                additionalLabels: new[] { "env: abo", "type: feature" },
                project: "release-next",
                status: StatusType.Planned);


            var result = new
            {
                message = $"Feature suggestion issue #{issue.Id}.",
                analysisIssue = new { id = issue.Id, title = issue.Title }
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Error suggesting feature: {ex.Message}";
        }
    }
}
