using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class GitTool : IAboTool
{
    private readonly IConnector _connector;

    public GitTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "git";
    public string Description => "Runs a git command in the project directory (e.g. 'status', 'add .', 'commit -m \"msg\"'). Do NOT include the 'git' executable name in the arguments.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            arguments = new { type = "string", description = "The arguments to pass to git (e.g., 'status', 'clone https://...')." }
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
                return await _connector.RunGitAsync(cmdArgs);
            }
            return "Error: arguments parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
