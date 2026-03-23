using System.Text.Json;
using Abo.Tools;

namespace Abo.Tools;

public class UpsertRoleTool : IAboTool
{
    private readonly string _rolesFile;

    public UpsertRoleTool()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data", "Roles");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }
        _rolesFile = Path.Combine(dataDir, "roles.json");
    }

    public string Name => "upsert_role";
    public string Description => "Creates or updates an AI organizational role definition. IMPORTANT: Before assigning a QA, Dev, or other role in a new BPMN process, you MUST ensure it exists here.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            roleId = new { type = "string", description = "The unique system ID for the role (e.g., Role_QA_Agent, Role_Backend_Dev). MUST start with Role_" },
            title = new { type = "string", description = "A human-readable title for the role (e.g., QA Test Engineer)." },
            systemPrompt = new { type = "string", description = "The complete, detailed AI system prompt that describes the exact behavior, rules, tools, and persona this role must follow." }
        },
        required = new[] { "roleId", "title", "systemPrompt" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        UpsertRoleArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<UpsertRoleArgs>(argumentsJson, jsOptions);
        }
        catch
        {
            return "Failed to parse arguments.";
        }

        if (args == null || string.IsNullOrWhiteSpace(args.RoleId) || string.IsNullOrWhiteSpace(args.SystemPrompt))
            return "Invalid role data provided.";

        if (!args.RoleId.StartsWith("Role_"))
        {
            return "Validation Error: roleId MUST start with 'Role_'.";
        }

        try
        {
            var roles = new List<RoleDefinition>();
            if (File.Exists(_rolesFile))
            {
                var existingJson = await File.ReadAllTextAsync(_rolesFile);
                if (!string.IsNullOrWhiteSpace(existingJson))
                {
                    roles = JsonSerializer.Deserialize<List<RoleDefinition>>(existingJson, jsOptions) ?? new();
                }
            }

            var existingRole = roles.FirstOrDefault(r => string.Equals(r.RoleId, args.RoleId, StringComparison.OrdinalIgnoreCase));
            if (existingRole != null)
            {
                existingRole.Title = args.Title;
                existingRole.SystemPrompt = args.SystemPrompt;
                await SaveRolesAsync(roles);
                return $"Successfully updated existing role '{args.RoleId}'.";
            }
            else
            {
                roles.Add(new RoleDefinition
                {
                    RoleId = args.RoleId,
                    Title = args.Title,
                    SystemPrompt = args.SystemPrompt
                });
                await SaveRolesAsync(roles);
                return $"Successfully created new role '{args.RoleId}'.";
            }
        }
        catch (Exception ex)
        {
            return $"Error upserting role: {ex.Message}";
        }
    }

    private async Task SaveRolesAsync(List<RoleDefinition> roles)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(_rolesFile, JsonSerializer.Serialize(roles, options));
    }

    private class UpsertRoleArgs
    {
        public string RoleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
    }

    private class RoleDefinition
    {
        public string RoleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
    }
}
