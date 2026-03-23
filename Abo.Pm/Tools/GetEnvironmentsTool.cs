using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools;

public class GetEnvironmentsTool : IAboTool
{
    private readonly string _environmentsFile;

    public GetEnvironmentsTool()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var environmentsDir = Path.Combine(dataDir, "Environments");
        _environmentsFile = Path.Combine(environmentsDir, "environments.json");
    }

    public string Name => "get_environments";
    public string Description => "Lists all configured environments available for projects to use. Environments define where a project resides (e.g. local directory).";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        if (!File.Exists(_environmentsFile))
        {
            return "No environments configured.";
        }

        try
        {
            var json = await File.ReadAllTextAsync(_environmentsFile);
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var environments = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(json, jsOptions);

            if (environments == null || !environments.Any())
            {
                return "No environments found in configuration.";
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("Available Environments:");
            output.AppendLine("-----------------------");

            foreach (var env in environments)
            {
                output.AppendLine($"- **Name**: {env.Name}");
                output.AppendLine($"  - Type: {env.Type}");
                output.AppendLine($"  - Os: {env.Os}");
                output.AppendLine($"  - Dir: {env.Dir}");
                output.AppendLine();
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading environments: {ex.Message}";
        }
    }
}
