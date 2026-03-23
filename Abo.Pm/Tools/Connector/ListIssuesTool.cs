using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class ListIssuesTool : IAboTool
{
    private readonly IIssueTrackerConnector _connector;

    public ListIssuesTool(IIssueTrackerConnector connector)
    {
        _connector = connector;
    }

    public string Name => "list_issues";
    public string Description => "Lists open issues or feature requests from the configured issue tracker (e.g., GitHub).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            state = new { type = "string", description = "The state of the issues to list (e.g., 'open', 'closed', 'all'). Default is 'open'." },
            labels = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Optional list of labels to filter the issues."
            }
        },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);

            string? state = null;
            if (args != null && args.TryGetValue("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String)
                state = stateElement.GetString();

            string[]? labels = null;
            if (args != null && args.TryGetValue("labels", out var labelsElement) && labelsElement.ValueKind == JsonValueKind.Array)
                labels = labelsElement.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray();

            var issues = await _connector.ListIssuesAsync(state, labels);
            return JsonSerializer.Serialize(issues, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
