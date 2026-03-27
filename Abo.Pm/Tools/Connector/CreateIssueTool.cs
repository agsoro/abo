using System.Text.Json;
using Abo.Contracts.Models;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class CreateIssueTool : IAboTool
{
    private readonly IIssueTrackerConnector _connector;

    public CreateIssueTool(IIssueTrackerConnector connector)
    {
        _connector = connector;
    }

    public string Name => "create_issue";
    public string Description => "Creates a new issue, feature request, or bug report in the configured issue tracker.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "The title of the issue." },
            body = new { type = "string", description = "The detailed body description of the issue. Use markdown as appropriate." },
            type = new
            {
                type = "string",
                description = "The type of the issue.",
                @enum = new[] { "feature", "bug", "improvement", "task", "chore", "doc" }
            },
            size = new { type = "string", description = "Optional relative size estimate, e.g. 'S', 'M', 'L'." },
            project = new { type = "string", description = "Optional project name to attach the issue to." }
        },
        required = new[] { "title", "body", "type" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("title", out var title) && args.TryGetValue("body", out var body) && args.TryGetValue("type", out var type))
            {
                if (!IssueType.IsValid(type))
                    return $"Error: Invalid type '{type}'. Allowed values: {string.Join(", ", IssueType.AllowedValues)}.";

                args.TryGetValue("size", out var size);
                args.TryGetValue("project", out var project);
                var issue = await _connector.CreateIssueAsync(title, body, type, size ?? string.Empty, null, project);
                return JsonSerializer.Serialize(issue, new JsonSerializerOptions { WriteIndented = true });
            }
            return "Error: title, body, and type parameters are required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
