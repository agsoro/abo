using System.Text.Json;
using Abo.Tools;

namespace Abo.Tools;

public class GetRolesTool : IAboTool
{
    private readonly string _rolesFile;

    public GetRolesTool()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data", "Roles");
        _rolesFile = Path.Combine(dataDir, "roles.json");
    }

    public string Name => "get_roles";
    public string Description => "Returns a list of all currently defined AI roles and their descriptions (system prompts). Use this before assigning a role in a new BPMN process to see what exists.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        if (!File.Exists(_rolesFile))
        {
            return "No roles have been defined yet. You can create them using the upsert_role tool.";
        }

        try
        {
            var existingJson = await File.ReadAllTextAsync(_rolesFile);
            if (string.IsNullOrWhiteSpace(existingJson))
                return "No roles have been defined yet. You can create them using the upsert_role tool.";

            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var roles = JsonSerializer.Deserialize<List<RoleDefinition>>(existingJson, jsOptions);

            if (roles == null || !roles.Any())
            {
                return "No roles have been defined yet. You can create them using the upsert_role tool.";
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Defined AI Roles\n");

            foreach (var role in roles)
            {
                output.AppendLine($"## {role.Title} (`{role.RoleId}`)");
                output.AppendLine("**System Prompt / Description:**");
                output.AppendLine(role.SystemPrompt);
                if (role.AllowedTools != null && role.AllowedTools.Any())
                {
                    output.AppendLine("**Allowed Tools:** " + string.Join(", ", role.AllowedTools));
                }
                output.AppendLine();
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading roles: {ex.Message}";
        }
    }

    private class RoleDefinition
    {
        public string RoleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public List<string> AllowedTools { get; set; } = new();
    }
}
