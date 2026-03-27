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

            // --- Sub-issue blocking logic ---
            // Build a parent → children map from "parent: <id>" labels on child issues
            var childrenByParentId = activeIssues
                .Where(i => i.Labels.Any(l => l.StartsWith("parent: ", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(i => ExtractLabelValue(i.Labels, "parent"))
                .Where(g => g.Key != null)
                .ToDictionary(g => g.Key!, g => g.ToList());

            // Determine which parent issues are blocked by at least one non-terminal child
            var blockedIssueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (parentId, children) in childrenByParentId)
            {
                bool hasBlockingChild = children.Any(c =>
                {
                    var childStep = Abo.Core.WorkflowEngine.ResolveStepIdFallback(c).ToLower();
                    // "open", "planned", "work", "review" block the parent; "check", "done", "invalid" do not
                    return childStep is "open" or "planned" or "work" or "review";
                });
                if (hasBlockingChild)
                {
                    blockedIssueIds.Add(parentId);
                }
            }

            // Filter: For 'planned' step, only surface issues with project = 'release-current'
            // Issues with project 'backlog' or 'release-next' at 'planned' step are deferred/backlog.
            activeIssues = activeIssues
                .Where(i =>
                {
                    var step = Abo.Core.WorkflowEngine.ResolveStepIdFallback(i).ToLower();
                    if (step == "planned")
                        return string.Equals(i.Project, "release-current", StringComparison.OrdinalIgnoreCase);
                    return true; // all other steps are always surfaced
                })
                .ToList();

            // Filter out blocked parents from the actionable work queue
            activeIssues = activeIssues
                .Where(i => !blockedIssueIds.Contains(i.Id))
                .OrderBy(i => GetStepPriority(Abo.Core.WorkflowEngine.ResolveStepIdFallback(i)))
                .ThenBy(i => i.Id)
                .ToList();

            if (!activeIssues.Any())
            {
                return "No open issue work found. All remaining issues are blocked by open sub-issues.";
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Open Work Items\n");

            // Priority rule banner
            output.AppendLine("> ⚠️ **PRIORITY RULE**: Pick the first listed issue. Issues are sorted: `open` > `release-planning` > `review` > `check` > `work` > `planned` (release-current only).");
            output.AppendLine("> Always process open (newly triaged) issues first, then release-planning. Among in-progress issues, prefer those closest to completion: review first, then check, work, planned.");
            if (blockedIssueIds.Any())
            {
                output.AppendLine($"> 🔒 **{blockedIssueIds.Count} parent issue(s) are hidden** because they have open sub-issues that must be completed first.");
            }
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
                    1 => "🟠 High — Awaiting Release Planning",
                    2 => "🟡 Medium-High — In QA Review (almost done)",
                    3 => "🟡 Medium — Awaiting Release Documentation",
                    4 => "🟢 Medium-Low — In Development",
                    5 => "🟢 Low — Planned (release-current, not yet in dev)",
                    _ => "⚪ Unknown"
                };

                // Indicate if this issue is itself a sub-issue
                var parentId = ExtractLabelValue(issue.Labels, "parent");
                var subIssueNote = parentId != null ? $" _(sub-issue of #{parentId})_" : string.Empty;

                // Indicate if this issue has sub-issues still in progress (sub-issue count info)
                var subIssueCount = childrenByParentId.TryGetValue(issue.Id, out var subs) ? subs.Count : 0;

                output.AppendLine($"### Issue: {issue.Title} (Ref: `{projRef}` | Issue: `{issue.Id}`){subIssueNote}");
                if (!string.IsNullOrWhiteSpace(issue.Project))
                    output.AppendLine($"- **Project**: `{issue.Project}`");
                output.AppendLine($"- **Environment**: `{envName}`");
                output.AppendLine($"- **Priority**: `{priorityLabel}`");
                output.AppendLine($"- **Issue Status**: `{issue.State}`");
                output.AppendLine($"- **Current Step**: {nodeName} (`{stepId}`)");

                var roleToShow = stepInfo?.Role?.RoleId ?? "Unknown";

                if (!string.IsNullOrWhiteSpace(roleToShow))
                    output.AppendLine($"- **Required Role**: `{roleToShow}`");

                var transitions = Abo.Core.WorkflowEngine.GetTransitions(stepId);
                if (transitions.Count > 0)
                {
                    var stepsDesc = string.Join(", ", transitions.Select(kvp => $"{kvp.Key} -> {kvp.Value.NextStepId}"));
                    output.AppendLine($"- **Next Steps**: {stepsDesc}");
                }
                output.AppendLine($"- **State**: {status}");
                if (subIssueCount > 0)
                    output.AppendLine($"- **Sub-issues**: {subIssueCount} linked sub-issue(s) (all in terminal/near-terminal state — parent is unblocked)");
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
    /// Open (newly triaged) issues are processed first, then release-planning.
    /// Among in-progress issues, prefer those closest to completion:
    ///   release-planning (1) > review (2) > check (3) > work (4) > planned (5).
    /// </summary>
    private static int GetStepPriority(string stepId) => stepId.ToLower() switch
    {
        "open"             => 0,
        "release-planning" => 1,   // NEW
        "review"           => 2,
        "check"            => 3,
        "work"             => 4,
        "planned"          => 5,
        _                  => 6
    };

    private string? ExtractLabelValue(IEnumerable<string> labels, string key)
    {
        var prefix = key + ": ";
        var match = labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?.Substring(prefix.Length).Trim();
    }
}
