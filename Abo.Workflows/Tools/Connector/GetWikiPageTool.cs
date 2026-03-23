using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class GetWikiPageTool : IAboTool
{
    private readonly IWikiConnector _wiki;

    public GetWikiPageTool(IWikiConnector wiki)
    {
        _wiki = wiki;
    }

    public string Name => "get_wiki_page";
    public string Description => "Retrieves the contents of a wiki page.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pathOrId = new { type = "string", description = "For filesystem wiki: relative markdown file path (e.g. 'architecture.md'). For XpectoLive wiki: the Page ID." }
        },
        required = new[] { "pathOrId" }
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var doc = JsonDocument.Parse(argumentsJson);
        var pathOrId = doc.RootElement.TryGetProperty("pathOrId", out var p) ? p.GetString() : null;

        if (string.IsNullOrWhiteSpace(pathOrId)) return "Error: pathOrId is required.";

        return await _wiki.GetPageAsync(pathOrId);
    }
}
