using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class ShellTool : IAboTool
{
    private readonly IWorkspaceConnector _connector;

    public ShellTool(IWorkspaceConnector connector)
    {
        _connector = connector;
    }

    public string Name => "shell";
    public string Description =>
        "Runs a shell command (e.g., 'pyenv', 'bash', a custom script) in the project directory. " +
        "Provide the executable name as 'command' and any arguments as 'arguments'. " +
        "Use for pyenv version management, running scripts, or tools not covered by git/dotnet/python tools.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The command/executable to run (e.g., 'pyenv', 'bash', 'npm')." },
            arguments = new { type = "string", description = "Arguments to pass to the command (e.g., 'install 3.11.9', 'local 3.11.9', 'versions')." }
        },
        required = new[] { "command", "arguments" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson);
            if (args != null &&
                args.TryGetValue("command", out var cmd) &&
                args.TryGetValue("arguments", out var cmdArgs))
            {
                return await _connector.RunShellAsync(cmd, cmdArgs);
            }
            return "Error: 'command' and 'arguments' parameters are required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
