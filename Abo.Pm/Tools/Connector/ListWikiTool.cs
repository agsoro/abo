using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class ListWikiTool : IAboTool
{
    private readonly IWikiConnector _wiki;

    public ListWikiTool(IWikiConnector wiki)
    {
        _wiki = wiki;
    }

    public string Name => "list_wiki";
    public string Description => "Lists the wiki content as a tree structure starting from the specified path.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "The relative path within the wiki (use '.' or empty string for the root directory)." }
        },
        required = new[] { "path" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var doc = JsonDocument.Parse(argumentsJson);
            var path = doc.RootElement.TryGetProperty("path", out var p) ? p.GetString() : ".";

            if (string.IsNullOrWhiteSpace(path)) path = ".";

            return await _wiki.ListWikiAsync(path);
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
