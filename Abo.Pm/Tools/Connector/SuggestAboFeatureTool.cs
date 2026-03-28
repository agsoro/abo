using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Abo.Tools;

namespace Abo.Tools.Connector;

/// <summary>
/// Allows agents to autonomously suggest new tools or capabilities for their work.
/// When invoked, this tool creates a new issue in the issue tracker with an analysis prompt
/// and optionally creates a sub-issue for implementation if the feature should be implemented.
/// </summary>
public class SuggestAboFeatureTool : IAboTool
{
    private readonly IEnumerable<IAboTool> _allTools;
    private readonly IIssueTrackerConnector _connector;

    public string Name => "suggest_abo_feature";
    public string Description => "Allows agents to autonomously suggest new tools or capabilities for their work. Creates an issue with an analysis prompt and optionally a sub-issue for implementation.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            description = new
            {
                type = "string",
                description = "The detailed description of the suggested feature or capability that would help the agent complete their work."
            }
        },
        required = new[] { "description" },
        additionalProperties = false
    };

    public SuggestAboFeatureTool(IEnumerable<IAboTool> allTools, IIssueTrackerConnector connector)
    {
        _allTools = allTools;
        _connector = connector;
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

            // Extract a potential tool name from the description for conflict checking
            // This is a heuristic: look for patterns like "add X tool" or "tool for X"
            var toolNameCandidate = ExtractToolName(description);

            // Check if a tool with this name already exists
            if (!string.IsNullOrWhiteSpace(toolNameCandidate))
            {
                var existingTool = _allTools.FirstOrDefault(t =>
                    t.Name.Equals(toolNameCandidate, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals($"add_{toolNameCandidate}", StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals($"{toolNameCandidate}_tool", StringComparison.OrdinalIgnoreCase));

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

            // Create the main analysis issue
            var issue = await _connector.CreateIssueAsync(
                title: title,
                body: body,
                type: IssueType.Feature,
                size: "S",
                additionalLabels: new[] { "env: abo", "type: feature" },
                project: "release-next",
                status: StatusType.Planned);

            // Create a sub-issue for implementation if approved
            var subIssueTitle = $"[abo] implement: {shortDescription}";
            var subIssueBody = $"++ implementation sub-issue for: {description} ++\n\n" +
                               "## Implementation Notes\n" +
                               "- **Triggered by**: Feature suggestion via `suggest_abo_feature` tool\n" +
                               "- **Parent Issue**: #" + issue.Id + "\n\n" +
                               "### Requirements\n" +
                               "[To be filled by analyst]\n\n" +
                               "### Technical Approach\n" +
                               "[To be determined during implementation planning]";

            var subIssue = await _connector.CreateIssueAsync(
                title: subIssueTitle,
                body: subIssueBody,
                type: IssueType.Feature,
                size: "",
                additionalLabels: new[] { $"parent: {issue.Id}", "env: abo", "type: feature" },
                project: "release-next",
                status: StatusType.Planned);

            // Establish GitHub native sub-issue link if possible
            if (!string.IsNullOrWhiteSpace(issue.NodeId) && !string.IsNullOrWhiteSpace(subIssue.NodeId))
            {
                await _connector.AddSubIssueAsync(issue.NodeId, subIssue.NodeId);
            }

            var result = new
            {
                message = $"Feature suggestion issue #{issue.Id} created successfully with implementation sub-issue #{subIssue.Id}.",
                analysisIssue = new { id = issue.Id, title = issue.Title, url = GetIssueUrl(issue) },
                implementationIssue = new { id = subIssue.Id, title = subIssue.Title, url = GetIssueUrl(subIssue) }
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Error suggesting feature: {ex.Message}";
        }
    }

    /// <summary>
    /// Extracts a potential tool name from the description using simple heuristics.
    /// </summary>
    private static string? ExtractToolName(string description)
    {
        // Look for patterns like "tool for X", "add X tool", "implement X"
        var lowerDesc = description.ToLowerInvariant();

        // Pattern: "add [something] tool" or "add [something]"
        var addMatch = System.Text.RegularExpressions.Regex.Match(
            lowerDesc, @"add\s+(?:a\s+)?(?:new\s+)?(?:tool\s+for\s+)?(.+?)(?:\s+tool)?$", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (addMatch.Success && addMatch.Groups.Count > 1)
        {
            return NormalizeToolName(addMatch.Groups[1].Value);
        }

        // Pattern: "tool for [something]"
        var toolForMatch = System.Text.RegularExpressions.Regex.Match(
            lowerDesc, @"tool\s+for\s+(.+?)(?:\s+to|$)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (toolForMatch.Success && toolForMatch.Groups.Count > 1)
        {
            return NormalizeToolName(toolForMatch.Groups[1].Value);
        }

        // Pattern: "implement [something]" or "create [something]"
        var implementMatch = System.Text.RegularExpressions.Regex.Match(
            lowerDesc, @"(?:implement|create|build)\s+(?:a\s+)?(.+?)(?:\s+tool)?$", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (implementMatch.Success && implementMatch.Groups.Count > 1)
        {
            return NormalizeToolName(implementMatch.Groups[1].Value);
        }

        return null;
    }

    /// <summary>
    /// Normalizes a tool name by removing special characters and converting to snake_case.
    /// </summary>
    private static string NormalizeToolName(string name)
    {
        // Remove common words
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            name, @"\b(a|an|the|new|for|to|that|this)\b", "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Replace spaces and special characters with underscores
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[\s\-]+", "_");

        // Remove any remaining non-alphanumeric characters (except underscores)
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^a-z0-9_]", "");

        // Remove leading/trailing underscores
        cleaned = cleaned.Trim('_');

        // Convert to lowercase
        return cleaned.ToLowerInvariant();
    }

    /// <summary>
    /// Gets a URL for the issue if available (placeholder implementation).
    /// </summary>
    private static string GetIssueUrl(IssueRecord issue)
    {
        // The actual URL would depend on the connector implementation
        // For GitHub: https://github.com/{owner}/{repo}/issues/{id}
        // For filesystem: local path
        // Return a generic reference for now
        if (!string.IsNullOrWhiteSpace(issue.Project))
        {
            return $"#{issue.Id} [{issue.Project}]";
        }
        return $"#{issue.Id}";
    }
}
