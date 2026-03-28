using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class GitTool : IAboTool
{
    private readonly IWorkspaceConnector _connector;

    public GitTool(IWorkspaceConnector connector)
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
            if (args == null || !args.TryGetValue("arguments", out var cmdArgs))
            {
                return "Error: arguments parameter is required.";
            }

            if (cmdArgs.Contains("../") || cmdArgs.Contains("..\\"))
            {
                return "Error: directory traversal is not allowed";
            }
            if (cmdArgs.EndsWith(".wiki"))
            {
                return "Error: use wiki tools instead";
            }
            if (cmdArgs.StartsWith("clone ") || cmdArgs.StartsWith("clone\t"))
            {
                return "Error: clone is not allowed";
            }
            if (cmdArgs.Contains("commit") && !cmdArgs.Contains("-m ") && !cmdArgs.Contains("--no-edit"))
            {
                return "Error: stdin is not allowed";
            }
            if (cmdArgs.Contains("rebase") && !cmdArgs.Contains("--no-edit"))
            {
                return "Error: stdin is not allowed";
            }
            if (cmdArgs.Contains("merge") && !cmdArgs.Contains("--no-edit"))
            {
                return "Error: stdin is not allowed";
            }
            if (cmdArgs.Contains("hash-object -w"))
            {
                return "Error: direct file writes are not allowed; use dedicated file operation methods";
            }
            if (cmdArgs.Contains("write-tree"))
            {
                return "Error: direct file writes are not allowed; use dedicated file operation methods";
            }
            if (cmdArgs.Contains("update-index --add"))
            {
                return "Error: direct file writes are not allowed; use dedicated file operation methods";
            }
            if (cmdArgs.Contains("checkout-index"))
            {
                return "Error: direct file writes are not allowed; use dedicated file operation methods";
            }

            return await _connector.RunGitAsync(cmdArgs);
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
