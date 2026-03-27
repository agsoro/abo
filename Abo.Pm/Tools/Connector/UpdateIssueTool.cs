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
    public string Description => "Updates the title, body and type of an existing issue. Use this to standardize issue titles to the format: 'type: component: reason' (5–15 words) and rephrase the body to a concise, technical description. Also used during triage to fill or correct the 'type' label. IMPORTANT: When rephrasing the title or body, the 'body' parameter MUST end with a preserved original submission section: '---\\n**Original submission:**\\n\\n*Original title:* <old title>\\n\\n*Original body:* <old body>'. Fetch the issue first via get_issue to capture the original text before overwriting.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            issueId = new { type = "string", description = "The ID or number of the issue to update." },
            title = new { type = "string", description = "The new title for the issue. Should follow the format: 'type: component: reason' (5–15 words, type = " + string.Join("/", IssueType.AllowedValues) + ")." },
            body = new { type = "string", description = "The new body/description for the issue. Should be rephrased to a clear, technical perspective with only necessary information. When rephrasing, always append the original submission at the bottom under '---\\n**Original submission:**\\n\\n*Original title:* <old title>\\n\\n*Original body:* <old body>'." },
            type = new
            {
                type = "string",
                description = "Set or correct the issue type.",
                @enum = IssueType.AllowedValues
            },
            size = new { type = "string", description = "Optional relative size estimate, e.g. 'S', 'M', 'L'." }
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

            string? typeToSet = null;
            if (args.TryGetValue("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                var newType = typeElement.GetString();
                if (!IssueType.IsValid(newType))
                    return $"Error: Invalid type '{newType}'. Allowed values: {string.Join(", ", IssueType.AllowedValues)}.";
                typeToSet = newType;
            }

            string? sizeToSet = null;
            if (args.TryGetValue("size", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.String)
            {
                sizeToSet = sizeElement.GetString();
            }

            var updated = await _connector.UpdateIssueAsync(issueId, title: title, body: body, type: typeToSet, size: sizeToSet);
            return JsonSerializer.Serialize(updated);
        }
        catch (Exception ex)
        {
            return $"Error updating issue: {ex.Message}";
        }
    }
}
