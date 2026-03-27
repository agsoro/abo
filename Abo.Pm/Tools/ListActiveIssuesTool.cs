using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Abo.Integrations.GitHub;
using Abo.Tools;
using Microsoft.Extensions.Configuration;

namespace Abo.Tools;

public class ListActiveIssuesTool : IAboTool
{
    private readonly IConfiguration _config;

    public ListActiveIssuesTool(IConfiguration config)
    {
         _config = config;
    }

    public string Name => "list_issues";
    public string Description => "Lists all currently active issues, subissues, their process types, and current state. This provides full visibility into running processes.";

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
                return "No active issues found.";
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Active Issues Hierarchy");

            // Look for roots (no parent label)
            var roots = activeIssues.Where(i => !i.Labels.Any(l => l.StartsWith("parent:"))).ToList();
            foreach (var root in roots)
            {
                AppendIssue(output, root, activeIssues, 0);
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading active issues: {ex.Message}";
        }
    }

    private void AppendIssue(System.Text.StringBuilder output, IssueRecord issue, List<IssueRecord> allIssues, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);
        
        var typeId = ExtractLabelValue(issue.Labels, "type") ?? "Unknown";
        var stepId = Abo.Core.WorkflowEngine.ResolveStepIdFallback(issue);
        var envName = ExtractLabelValue(issue.Labels, "env") ?? "Unknown";
        var projRef = ExtractLabelValue(issue.Labels, "ref") ?? issue.Id;
        var project = issue.Project;

        var stepInfo = Abo.Core.WorkflowEngine.GetStepInfo(issue);
        var roleToShow = stepInfo?.Role?.RoleId ?? "Unknown";

        var transitions = Abo.Core.WorkflowEngine.GetTransitions(issue);
        var nextSteps = transitions.Count > 0 
            ? string.Join(", ", transitions.Select(kvp => $"{kvp.Key} -> {kvp.Value.NextStepId}"))
            : "None";

        output.AppendLine($"{indent}- **[Ref: {projRef} | Issue: {issue.Id}] {issue.Title}**");
        if (!string.IsNullOrWhiteSpace(project))
            output.AppendLine($"{indent}  - Project: `{project}`");
        output.AppendLine($"{indent}  - Type: `{typeId}`");
        output.AppendLine($"{indent}  - Step: `{stepId}`");
        output.AppendLine($"{indent}  - Role: `{roleToShow}`");
        output.AppendLine($"{indent}  - Next Steps: `{nextSteps}`");
        output.AppendLine($"{indent}  - Status: `{issue.State}`");
        output.AppendLine($"{indent}  - Environment: `{envName}`");

        var children = allIssues.Where(i => ExtractLabelValue(i.Labels, "parent") == projRef || ExtractLabelValue(i.Labels, "parent") == issue.Id).ToList();
        foreach (var child in children)
        {
            AppendIssue(output, child, allIssues, indentLevel + 1);
        }
    }

    private string? ExtractLabelValue(IEnumerable<string> labels, string key)
    {
        var prefix = key + ": ";
        var match = labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?.Substring(prefix.Length).Trim();
    }
}
