using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class PatchWikiPageTool : IAboTool
{
    private readonly IWikiConnector _wiki;

    public PatchWikiPageTool(IWikiConnector wiki)
    {
        _wiki = wiki;
    }

    public string Name => "patch_wiki_page";
    public string Description => "Applies a unified diff/patch to a wiki page for targeted modifications without full content rewrite.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            pathOrId = new { type = "string", description = "For filesystem wiki: relative markdown file path. For XpectoLive wiki: the Page ID." },
            patch = new { type = "string", description = "The unified diff/patch content to apply." }
        },
        required = new[] { "pathOrId", "patch" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("pathOrId", out var pathOrId) && args.TryGetValue("patch", out var patch))
            {
                return await _wiki.PatchPageAsync(pathOrId, patch);
            }
            return "Error: pathOrId and patch parameters are required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
