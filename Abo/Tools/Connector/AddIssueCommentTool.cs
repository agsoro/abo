using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class AddIssueCommentTool : IAboTool
{
    private readonly IIssueTrackerConnector _connector;

    public AddIssueCommentTool(IIssueTrackerConnector connector)
    {
        _connector = connector;
    }

    public string Name => "add_issue_comment";
    public string Description => "Adds a comment to an existing issue in the project's configured issue tracker (e.g. to link a completed task or commit ID).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            issueId = new { type = "string", description = "The ID or number of the issue to comment on." },
            body = new { type = "string", description = "The content of the comment in markdown." }
        },
        required = new[] { "issueId", "body" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("issueId", out var issueId) && args.TryGetValue("body", out var body))
            {
                return await _connector.AddIssueCommentAsync(issueId, body);
            }
            return "Error: issueId and body parameters are required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
