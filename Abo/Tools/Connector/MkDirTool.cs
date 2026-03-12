using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class MkDirTool : IAboTool
{
    private readonly IConnector _connector;

    public MkDirTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "mkdir";
    public string Description => "Creates a new directory in the project.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            relativePath = new { type = "string", description = "The relative path of the new directory to create." }
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
                return await _connector.MkDirAsync(relativePath);
            }
            return "Error: relativePath parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
