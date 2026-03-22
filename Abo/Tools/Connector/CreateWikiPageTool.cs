using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class CreateWikiPageTool : IAboTool
{
    private readonly IWikiConnector _wiki;

    public CreateWikiPageTool(IWikiConnector wiki)
    {
        _wiki = wiki;
    }

    public string Name => "create_wiki_page";
    public string Description => "Creates a new wiki page.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "Title of the new wiki page." },
            content = new { type = "string", description = "Markdown content for the new page." },
            parentPathOrId = new { type = "string", description = "(Optional) For filesystem: relative parent directory path. For XpectoLive: parent Page ID." }
        },
        required = new[] { "title", "content" }
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var doc = JsonDocument.Parse(argumentsJson);
        var title = doc.RootElement.GetProperty("title").GetString();
        var content = doc.RootElement.GetProperty("content").GetString();
        var parentPathOrId = doc.RootElement.TryGetProperty("parentPathOrId", out var pp) ? pp.GetString() : null;

        if (string.IsNullOrWhiteSpace(title)) return "Error: title is required.";

        return await _wiki.CreatePageAsync(title, content ?? "", parentPathOrId);
    }
}
