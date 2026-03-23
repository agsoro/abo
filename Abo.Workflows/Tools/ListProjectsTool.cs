using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Abo.Integrations.GitHub;
using Abo.Tools;
using Microsoft.Extensions.Configuration;

namespace Abo.Tools;

public class ListProjectsTool : IAboTool
{
    private readonly IConfiguration _config;

    public ListProjectsTool(IConfiguration config)
    {
         _config = config;
    }

    public string Name => "list_projects";
    public string Description => "Lists all currently active projects, subprojects, their process types, and current state. This provides full visibility into running processes.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "Environments", "environments.json");
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var envs = new List<ConnectorEnvironment>();
            
            if (File.Exists(environmentsFile))
            {
                var envJson = await File.ReadAllTextAsync(environmentsFile);
                envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envJson, jsOptions) ?? new();
            }

            var activeIssues = new List<IssueRecord>();

            if (envs.Any())
            {
                foreach (var env in envs.Where(e => e.IssueTracker != null))
                {
                    IIssueTrackerConnector? tracker = null;
                    if (env.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = _config["Integrations:GitHub:Token"];
                        tracker = new GitHubIssueTrackerConnector(env.IssueTracker, token, env.Name);
                    }
                    else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                    {
                        tracker = new FileSystemIssueTrackerConnector(env.Name);
                    }

                    if (tracker != null)
                    {
                        var issues = await tracker.ListIssuesAsync(state: "open");
                        activeIssues.AddRange(issues);
                    }
                }
            }


            if (!activeIssues.Any())
            {
                return "No active projects found.";
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Active Projects Hierarchy");

            // Look for roots (no parent label)
            var roots = activeIssues.Where(i => !i.Labels.Any(l => l.StartsWith("parent:"))).ToList();
            foreach (var root in roots)
            {
                AppendProject(output, root, activeIssues, 0);
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading active projects: {ex.Message}";
        }
    }

    private void AppendProject(System.Text.StringBuilder output, IssueRecord issue, List<IssueRecord> allIssues, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);
        
        var typeId = ExtractLabelValue(issue.Labels, "type") ?? "Unknown";
        var stepId = ExtractLabelValue(issue.Labels, "step") ?? "Unknown";
        var role = ExtractLabelValue(issue.Labels, "role") ?? "Unknown";
        var envName = ExtractLabelValue(issue.Labels, "env") ?? "Unknown";
        var projRef = ExtractLabelValue(issue.Labels, "ref") ?? issue.Id;

        output.AppendLine($"{indent}- **[Ref: {projRef} | Issue: {issue.Id}] {issue.Title}**");
        output.AppendLine($"{indent}  - Type: `{typeId}`");
        output.AppendLine($"{indent}  - Step: `{stepId}`");
        output.AppendLine($"{indent}  - Role: `{role}`");
        output.AppendLine($"{indent}  - Status: `{issue.State}`");
        output.AppendLine($"{indent}  - Environment: `{envName}`");

        var children = allIssues.Where(i => ExtractLabelValue(i.Labels, "parent") == projRef || ExtractLabelValue(i.Labels, "parent") == issue.Id).ToList();
        foreach (var child in children)
        {
            AppendProject(output, child, allIssues, indentLevel + 1);
        }
    }

    private string? ExtractLabelValue(IEnumerable<string> labels, string key)
    {
        var prefix = key + ": ";
        var match = labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?.Substring(prefix.Length).Trim();
    }
}
