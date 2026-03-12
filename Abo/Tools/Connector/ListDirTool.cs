using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class ListDirTool : IAboTool
{
    private readonly IConnector _connector;

    public ListDirTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "list_dir";
    public string Description => "Lists the contents (files and subdirectories) of a directory within the project.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            relativePath = new { type = "string", description = "The relative path to the directory (use '.' or empty string for the root directory)." }
        },
        required = new[] { "relativePath" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("relativePath", out var relativePath))
            {
                if (string.IsNullOrWhiteSpace(relativePath)) relativePath = ".";
                return await _connector.ListDirAsync(relativePath);
            }
            return "Error: relativePath parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
