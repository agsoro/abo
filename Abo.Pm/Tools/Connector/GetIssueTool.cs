using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class GetIssueTool : IAboTool
{
    private readonly IIssueTrackerConnector _connector;

    public GetIssueTool(IIssueTrackerConnector connector)
    {
        _connector = connector;
    }

    public string Name => "get_issue";
    public string Description => "Gets the details of a specific issue by ID from the configured issue tracker.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            issueId = new { type = "string", description = "The ID or number of the issue to retrieve." }
        },
        required = new[] { "issueId" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("issueId", out var issueId))
            {
                var issue = await _connector.GetIssueAsync(issueId);
                return issue != null ? JsonSerializer.Serialize(issue, new JsonSerializerOptions { WriteIndented = true }) : "Issue not found.";
            }
            return "Error: issueId parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
