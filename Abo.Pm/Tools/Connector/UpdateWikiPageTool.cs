using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class UpdateWikiPageTool : IAboTool
{
    private readonly IWikiConnector _wiki;

    public UpdateWikiPageTool(IWikiConnector wiki)
    {
        _wiki = wiki;
    }

    public string Name => "update_wiki_page";
    public string Description => "Updates the contents of an existing wiki page. WARNING: Overwrites entirely.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pathOrId = new { type = "string", description = "For filesystem wiki: relative markdown file path. For XpectoLive wiki: the Page ID." },
            content = new { type = "string", description = "Markdown content replacing the page." }
        },
        required = new[] { "pathOrId", "content" }
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var doc = JsonDocument.Parse(argumentsJson);
        var pathOrId = doc.RootElement.GetProperty("pathOrId").GetString();
        var content = doc.RootElement.GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(pathOrId)) return "Error: pathOrId is required.";

        return await _wiki.UpdatePageAsync(pathOrId, content ?? "");
    }
}
