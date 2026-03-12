using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class ReadFileTool : IAboTool
{
    private readonly IConnector _connector;

    public ReadFileTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "read_file";
    public string Description => "Reads the contents of a file in the project directory using the provided relative path.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            relativePath = new { type = "string", description = "The relative path to the file to open (e.g., 'src/main.cs')." }
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
                return await _connector.ReadFileAsync(relativePath);
            }
            return "Error: relativePath parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
