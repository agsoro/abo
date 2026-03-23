using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class MoveWikiPageTool : IAboTool
{
    private readonly IWikiConnector _wiki;

    public MoveWikiPageTool(IWikiConnector wiki)
    {
        _wiki = wiki;
    }

    public string Name => "move_wiki_page";
    public string Description => "Moves (and optionally renames) an existing wiki page to a new parent location.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pathOrId = new { type = "string", description = "For filesystem wiki: relative markdown file path of the page to move. For XpectoLive wiki: the Page ID." },
            newPathOrParentId = new { type = "string", description = "For filesystem wiki: relative path of the target parent directory. For XpectoLive wiki: the target parent Page ID. Use an empty string to move to the root." },
            newTitle = new { type = "string", description = "Optional. New title for the page. For filesystem wiki, this also determines the new filename slug." }
        },
        required = new[] { "pathOrId", "newPathOrParentId" }
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("pathOrId", out var pathOrIdEl) || string.IsNullOrWhiteSpace(pathOrIdEl.GetString()))
            return "Error: pathOrId is required.";

        if (!root.TryGetProperty("newPathOrParentId", out var newPathEl))
            return "Error: newPathOrParentId is required.";

        var pathOrId = pathOrIdEl.GetString()!;
        var newPathOrParentId = newPathEl.GetString() ?? "";

        string? newTitle = null;
        if (root.TryGetProperty("newTitle", out var newTitleEl) && newTitleEl.ValueKind == JsonValueKind.String)
        {
            newTitle = newTitleEl.GetString();
            if (string.IsNullOrWhiteSpace(newTitle)) newTitle = null;
        }

        return await _wiki.MovePageAsync(pathOrId, newPathOrParentId, newTitle);
    }
}
