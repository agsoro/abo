using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class SearchWikiTool : IAboTool
{
    private readonly IWikiConnector _wiki;

    public SearchWikiTool(IWikiConnector wiki)
    {
        _wiki = wiki;
    }

    public string Name => "search_wiki";
    public string Description => "Searches for content or titles in the configured wiki.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Text to search for." }
        },
        required = new[] { "query" }
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var doc = JsonDocument.Parse(argumentsJson);
        var query = doc.RootElement.GetProperty("query").GetString();

        if (string.IsNullOrWhiteSpace(query)) return "Error: query is required.";

        return await _wiki.SearchPagesAsync(query);
    }
}
