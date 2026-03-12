using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class PythonTool : IAboTool
{
    private readonly IConnector _connector;

    public PythonTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "python";
    public string Description => "Runs a python command in the project directory (e.g. 'script.py', '-m pytest', '-m venv .venv'). Do NOT include the 'python' executable name in the arguments.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            arguments = new { type = "string", description = "The arguments to pass to python (e.g., 'main.py', '-m pytest', '-m pip install -r requirements.txt')." }
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
                return await _connector.RunPythonAsync(cmdArgs);
            }
            return "Error: arguments parameter is required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
