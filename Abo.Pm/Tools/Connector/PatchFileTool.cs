using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class PatchFileTool : IAboTool
{
    private readonly IWorkspaceConnector _connector;

    public PatchFileTool(IWorkspaceConnector connector)
    {
        _connector = connector;
    }

    public string Name => "patch_file";
    public string Description => "Applies a unified diff/patch to a file for targeted modifications without full file rewrite.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            relativePath = new { type = "string", description = "The relative path to the file (e.g., 'src/main.cs')." },
            patch = new { type = "string", description = "The unified diff/patch content to apply." }
        },
        required = new[] { "relativePath", "patch" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("relativePath", out var relativePath) && args.TryGetValue("patch", out var patch))
            {
                return await _connector.PatchFileAsync(relativePath, patch);
            }
            return "Error: relativePath and patch parameters are required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
