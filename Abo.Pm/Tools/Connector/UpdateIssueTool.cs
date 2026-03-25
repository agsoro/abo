using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class UpdateIssueTool : IAboTool
{
    private readonly IIssueTrackerConnector _connector;

    public UpdateIssueTool(IIssueTrackerConnector connector)
    {
        _connector = connector;
    }

    public string Name => "update_issue";
    public string Description => "Updates the title, body, and/or type of an existing issue. Use this to standardize issue titles to the format: 'type: component: reason' (5–15 words) and rephrase the body to a concise, technical description. Also used during triage to fill or correct the 'type' label.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            issueId = new { type = "string", description = "The ID or number of the issue to update." },
            title = new { type = "string", description = "Optional. The new title for the issue. Should follow the format: 'type: component: reason' (5–15 words, type = feature/bug/improvement)." },
            body = new { type = "string", description = "Optional. The new body/description for the issue. Should be rephrased to a clear, technical perspective with only necessary information." },
            type = new
            {
                type = "string",
                description = "Optional. Set or correct the issue type label. Used during triage to fill a missing or invalid type.",
                @enum = new[] { "feature", "bug", "improvement", "task", "chore" }
            },
        },
        required = new[] { "issueId" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
            if (args == null || !args.TryGetValue("issueId", out var issueIdElement))
                return "Error: issueId parameter is required.";

            var issueId = issueIdElement.GetString();
            if (string.IsNullOrWhiteSpace(issueId))
                return "Error: issueId must not be empty.";

            string? title = null;
            string? body = null;
            string[]? labels = null;

            if (args.TryGetValue("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
                title = titleElement.GetString();

            if (args.TryGetValue("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String)
                body = bodyElement.GetString();

            if (args.TryGetValue("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                var newType = typeElement.GetString();
                if (!IssueType.IsValid(newType))
                    return $"Error: Invalid type '{newType}'. Allowed values: {string.Join(", ", IssueType.AllowedValues)}.";

                var existing = await _connector.GetIssueAsync(issueId);
                if (existing != null)
                {
                    existing.Labels.RemoveAll(l => l.StartsWith("type: ", StringComparison.OrdinalIgnoreCase));
                    existing.Labels.Add($"type: {newType}");
                    labels = existing.Labels.ToArray();
                }
            }

            var updated = await _connector.UpdateIssueAsync(issueId, title: title, body: body, labels: labels);
            return JsonSerializer.Serialize(updated);
        }
        catch (Exception ex)
        {
            return $"Error updating issue: {ex.Message}";
        }
    }
}
