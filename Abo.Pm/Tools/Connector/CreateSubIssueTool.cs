using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Microsoft.Extensions.Configuration;

namespace Abo.Tools.Connector;

/// <summary>
/// Creates a sub-issue linked to a parent issue.
/// The sub-issue automatically inherits the parent's project and environment label.
/// A "parent: &lt;parentIssueId&gt;" label is added to the child for label-based tracking,
/// and the GitHub native sub-issue link is established via the GraphQL addSubIssue mutation
/// (with graceful degradation if the API is unavailable).
/// </summary>
public class CreateSubIssueTool : IAboTool
{
    private readonly IIssueTrackerConnector _connector;

    public CreateSubIssueTool(IIssueTrackerConnector connector)
    {
        _connector = connector;
    }

    public string Name => "create_sub_issue";

    public string Description =>
        "Creates a new sub-issue linked to a parent issue. " +
        "The sub-issue automatically inherits the parent's project and environment label. " +
        "A 'parent: <id>' label is added for tracking, and GitHub's native sub-issue link is established where available.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            parentIssueId = new { type = "string", description = "The numeric ID of the parent issue that this sub-issue belongs to." },
            title = new { type = "string", description = "The title of the sub-issue." },
            body = new { type = "string", description = "The detailed body description of the sub-issue. Use markdown as appropriate." },
            type = new
            {
                type = "string",
                description = "The type of the sub-issue.",
                @enum = new[] { "feature", "bug", "improvement", "task", "chore", "doc" }
            },
            size = new { type = "string", description = "Optional relative size estimate, e.g. 'S', 'M', 'L'." }
        },
        required = new[] { "parentIssueId", "title", "body", "type" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args == null ||
                !args.TryGetValue("parentIssueId", out var parentIssueId) ||
                !args.TryGetValue("title", out var title) ||
                !args.TryGetValue("body", out var body) ||
                !args.TryGetValue("type", out var type))
            {
                return "Error: parentIssueId, title, body, and type parameters are required.";
            }

            if (!IssueType.IsValid(type))
                return $"Error: Invalid type '{type}'. Allowed values: {string.Join(", ", IssueType.AllowedValues)}.";

            args.TryGetValue("size", out var size);

            // Step 1: Fetch parent issue to inherit project and env label
            var parent = await _connector.GetIssueAsync(parentIssueId);
            if (parent == null)
            {
                return $"Error: Parent issue '{parentIssueId}' not found.";
            }

            var parentProject = parent.Project;

            // Extract env label from parent
            var envLabel = parent.Labels
                .FirstOrDefault(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase));

            // Build additional labels for the sub-issue
            var additionalLabels = new List<string>
            {
                $"parent: {parentIssueId}"
            };
            if (!string.IsNullOrWhiteSpace(envLabel))
            {
                additionalLabels.Add(envLabel);
            }

            // Step 2: Create the sub-issue inheriting the parent's project at the "open" step
            var subIssue = await _connector.CreateIssueAsync(
                title,
                body,
                type,
                size ?? string.Empty,
                additionalLabels: additionalLabels.ToArray(),
                project: parentProject,
                status: "open");

            // Step 3: Establish the GitHub native sub-issue link (graceful degradation on failure)
            if (!string.IsNullOrWhiteSpace(parent.NodeId) && !string.IsNullOrWhiteSpace(subIssue.NodeId))
            {
                var linked = await _connector.AddSubIssueAsync(parent.NodeId, subIssue.NodeId);
                if (!linked)
                {
                    // Non-fatal: label-based tracking via "parent: <id>" is still in place
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[CreateSubIssueTool] Native sub-issue link could not be established. Label-based tracking is active.");
                    Console.ResetColor();
                }
            }

            var result = new
            {
                message = $"Sub-issue #{subIssue.Id} created successfully and linked to parent #{parentIssueId}.",
                subIssue
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Error creating sub-issue: {ex.Message}";
        }
    }
}
