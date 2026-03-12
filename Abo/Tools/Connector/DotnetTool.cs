using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class DotnetTool : IAboTool
{
    private readonly IConnector _connector;

    public DotnetTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "dotnet";
    public string Description => "Runs a dotnet command in the project directory (e.g. 'build', 'run', 'test'). Do NOT include the 'dotnet' executable name in the arguments.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            arguments = new { type = "string", description = "The arguments to pass to dotnet (e.g., 'build', 'new console')." }
        },
        required = new[] { "arguments" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null && args.TryGetValue("arguments", out var cmdArgs))
            {
                return await _connector.RunDotnetAsync(cmdArgs);
            }
            return "Error: arguments parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
