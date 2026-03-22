using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class DeleteFileTool : IAboTool
{
    private readonly IWorkspaceConnector _connector;

    public DeleteFileTool(IWorkspaceConnector connector)
    {
        _connector = connector;
    }

    public string Name => "delete_file";
    public string Description => "Deletes a file from the project directory.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            relativePath = new { type = "string", description = "The relative path to the file to delete." }
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
                return await _connector.DeleteFileAsync(relativePath);
            }
            return "Error: relativePath parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
