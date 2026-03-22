using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class WriteFileTool : IAboTool
{
    private readonly IWorkspaceConnector _connector;

    public WriteFileTool(IWorkspaceConnector connector)
    {
        _connector = connector;
    }

    public string Name => "write_file";
    public string Description => "Writes contents to a file in the project directory using the provided relative path. Creates the file if it doesn't exist, overwrites if it does.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            relativePath = new { type = "string", description = "The relative path to the file (e.g., 'src/main.cs')." },
            content = new { type = "string", description = "The text content to write to the file." }
        },
        required = new[] { "relativePath", "content" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("relativePath", out var relativePath) && args.TryGetValue("content", out var content))
            {
                return await _connector.WriteFileAsync(relativePath, content);
            }
            return "Error: relativePath and content parameters are required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
