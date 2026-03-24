using System.Text.Json;
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
    public string Description => "Updates the title and/or body of an existing issue in the configured issue tracker. Use this to standardize issue titles to the format: 'type: component: reason' (5–15 words, where type = feature/bug/improvement) and rephrase the body to a concise, technical description containing only necessary information.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            issueId = new { type = "string", description = "The ID or number of the issue to update." },
            title = new { type = "string", description = "Optional. The new title for the issue. Should follow the format: 'type: component: reason' (5–15 words, type = feature/bug/improvement)." },
            body = new { type = "string", description = "Optional. The new body/description for the issue. Should be rephrased to a clear, technical perspective with only necessary information." }
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

            if (args.TryGetValue("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
                title = titleElement.GetString();

            if (args.TryGetValue("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String)
                body = bodyElement.GetString();

            var updated = await _connector.UpdateIssueAsync(issueId, title: title, body: body);
            return JsonSerializer.Serialize(updated);
        }
        catch (Exception ex)
        {
            return $"Error updating issue: {ex.Message}";
        }
    }
}
