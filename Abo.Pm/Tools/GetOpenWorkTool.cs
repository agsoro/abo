using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Abo.Integrations.GitHub;
using Abo.Tools;
using Microsoft.Extensions.Configuration;

namespace Abo.Tools;

public class GetOpenWorkTool : IAboTool
{
    private readonly IConfiguration _config;

    public GetOpenWorkTool(IConfiguration config)
    {
        _config = config;
    }

    public string Name => "get_open_work";
    public string Description => "Returns a structured list of open work.";

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
                return "No open issue work found.";
            }

            // Sort issues by step priority so newly triaged (open) issues appear first
            activeIssues = activeIssues
                .OrderBy(i => GetStepPriority(Abo.Core.WorkflowEngine.ResolveStepIdFallback(i)))
                .ThenBy(i => i.Id)
                .ToList();

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Open Work Items\n");

            // Priority rule banner
            output.AppendLine("> ⚠️ **PRIORITY RULE**: Pick the first listed issue. Issues are sorted: `open` > `planned` > `work` > `check` > `review`.");
            output.AppendLine("> Always process open (newly triaged) issues first. Only pick up planned or in-progress issues when no open issues remain.");
            output.AppendLine();

            foreach (var issue in activeIssues)
            {
                var typeId = ExtractLabelValue(issue.Labels, "type") ?? "Unknown";
                var stepId = Abo.Core.WorkflowEngine.ResolveStepIdFallback(issue);
                var envName = ExtractLabelValue(issue.Labels, "env") ?? "Unknown";
                var projRef = ExtractLabelValue(issue.Labels, "ref") ?? issue.Id;

                var stepInfo = Abo.Core.WorkflowEngine.GetStepInfo(stepId);
                string nodeName = stepInfo?.StepName ?? stepId;
                string status = stepInfo != null ? "Ready for work" : "Unknown State";
                if (string.Equals(stepId, "done", StringComparison.OrdinalIgnoreCase) || string.Equals(stepId, "invalid", StringComparison.OrdinalIgnoreCase))
                {
                    status = "Completed";
                }

                var priority = GetStepPriority(stepId);
                var priorityLabel = priority switch
                {
                    0 => "🔴 Highest — New Request (open)",
                    1 => "🟠 High — Planned (needs dev)",
                    2 => "🟡 Medium — In Development",
                    3 => "🟢 Low — Awaiting Release",
                    4 => "🟢 Low — In Review",
                    _ => "⚪ Unknown"
                };

                output.AppendLine($"### Issue: {issue.Title} (Ref: `{projRef}` | Issue: `{issue.Id}`)");
                if (!string.IsNullOrWhiteSpace(issue.Project))
                    output.AppendLine($"- **Project**: `{issue.Project}`");
                output.AppendLine($"- **Environment**: `{envName}`");
                output.AppendLine($"- **Priority**: `{priorityLabel}`");
                output.AppendLine($"- **Issue Status**: `{issue.State}`");
                output.AppendLine($"- **Current Step**: {nodeName} (`{stepId}`)");

                var roleToShow = stepInfo?.RequiredRole ?? "Unknown";

                if (!string.IsNullOrWhiteSpace(roleToShow))
                    output.AppendLine($"- **Required Role**: `{roleToShow}`");

                var transitions = Abo.Core.WorkflowEngine.GetTransitions(stepId);
                if (transitions.Any())
                {
                    var stepsDesc = string.Join(", ", transitions.Select(t => $"{t.ConditionName} -> {t.NextStepId}"));
                    output.AppendLine($"- **Next Steps**: {stepsDesc}");
                }
                output.AppendLine($"- **State**: {status}");
                output.AppendLine($"- **Action**: Run `checkout_task {{\\\"issueId\\\": \\\"{issue.Id}\\\"}}` to pick up this work.");
                output.AppendLine();
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading open work: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns a sort priority for a given step ID.
    /// Lower number = higher priority (should be worked on first).
    /// Open (newly triaged) issues are processed first; in-progress issues are picked last.
    /// </summary>
    private static int GetStepPriority(string stepId) => stepId.ToLower() switch
    {
        "open"    => 0,
        "planned" => 1,
        "work"    => 2,
        "check"   => 3,
        "review"  => 4,
        _         => 5
    };

    private string? ExtractLabelValue(IEnumerable<string> labels, string key)
    {
        var prefix = key + ": ";
        var match = labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?.Substring(prefix.Length).Trim();
    }
}
