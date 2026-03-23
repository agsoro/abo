using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class ReadFileTool : IAboTool
{
    private readonly IWorkspaceConnector _connector;

    public ReadFileTool(IWorkspaceConnector connector)
    {
        _connector = connector;
    }

    public string Name => "read_file";

    public string Description =>
        "Reads the contents of a file in the project directory using the provided relative path. " +
        "Files are limited to 50KB by default. Set 'important' to true to raise the limit to 250KB for " +
        "critical files — use sparingly as this consumes significantly more resources.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            relativePath = new
            {
                type = "string",
                description = "The relative path to the file to open (e.g., 'src/main.cs')."
            },
            important = new
            {
                type = "boolean",
                description = "Optional. Set to true to raise the read size limit from 50KB to 250KB for critical files. Costs more resources — use only when necessary."
            }
        },
        required = new[] { "relativePath" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            // relativePath is required
            if (!root.TryGetProperty("relativePath", out var relativePathElement))
                return "Error: relativePath parameter is required.";

            var relativePath = relativePathElement.GetString();
            if (string.IsNullOrWhiteSpace(relativePath))
                return "Error: relativePath parameter cannot be empty.";

            // important is optional, defaults to false
            bool important = false;
            if (root.TryGetProperty("important", out var importantElement)
                && importantElement.ValueKind == JsonValueKind.True)
            {
                important = true;
            }

            return await _connector.ReadFileAsync(relativePath, important);
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
