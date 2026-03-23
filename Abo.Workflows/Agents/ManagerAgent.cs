using System.Text.Json;
using Abo.Contracts.OpenAI;
using Abo.Core;
using Abo.Tools;
using Abo.Integrations.XpectoLive;

namespace Abo.Agents;

public class ManagerAgent : IAgent
{
    private readonly IEnumerable<IAboTool> _globalTools;
    private readonly Orchestrator _orchestrator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManagerAgent> _logger;
    private readonly IXpectoLiveWikiClient _wikiClient;

    public string Name => "ManagerAgent";
    public string Description => "The Project Lead / Manager. Identifies open tasks from active projects and delegates them to specialized agents who do the actual work.";
    public bool RequiresCapableModel => false;
    public bool RequiresReviewModel => false;

    public ManagerAgent(IEnumerable<IAboTool> globalTools, Orchestrator orchestrator, IConfiguration configuration, ILogger<ManagerAgent> logger, IXpectoLiveWikiClient wikiClient)
    {
        _globalTools = globalTools;
        _orchestrator = orchestrator;
        _configuration = configuration;
        _logger = logger;
        _wikiClient = wikiClient;
    }

    public string SystemPrompt =>
        "You are the ManagerAgent (Project Lead). Your goal is to oversee running projects and assign tasks to the correct specialists.\n\n" +
        "### WORKFLOW:\n" +
        "1. **Find Work**: Use `get_open_work` to see all active projects, their current step, and status. Find a project that has work to do.\n" +
        "2. **Determine Role**: Look at the current step of the project. Pay attention to the explicit `RequiredRole` emitted by `get_open_work`. Do NOT guess the role or use `get_roles` unless the required role is entirely missing or unmappable.\n" +
        "3. **Delegate Task**: Once you know the project and the required role, use `delegate_task` to assign the work to a SpecialistAgent. You must provide the `projectId`, the `roleId`, and detailed `instructions` on what they should do.\n" +
        "4. **Completion**: The `delegate_task` tool will synchronously execute the specialist. Calling this tool will terminate your current manager assignment, since you have successfully handed the work off.\n\n" +
        "### RULES:\n" +
        "- You must use `delegate_task` to get the actual work done.\n" +
        "- Be clear in your instructions to the specialist. Include that they must use `checkout_task` first.";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();

        var allowedGlobalTools = new[] { "list_projects", "get_open_work", "get_roles" };
        foreach (var tool in _globalTools.Where(t => allowedGlobalTools.Contains(t.Name)))
        {
            definitions.Add(CreateDef(tool));
        }

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "delegate_task",
                Description = "Delegates a task to a SpecialistAgent. This sets up the specialist and terminates your loop.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        projectId = new { type = "string", description = "The ID of the project the specialist should work on." },
                        roleId = new { type = "string", description = "The ID of the role the specialist should adopt." },
                        instructions = new { type = "string", description = "Detailed instructions for the specialist." }
                    },
                    required = new[] { "projectId", "roleId", "instructions" }
                }
            }
        });

        return definitions;
    }

    private ToolDefinition CreateDef(IAboTool tool)
    {
        return new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.ParametersSchema
            }
        };
    }

    public async Task<string> HandleToolCallAsync(ToolCall toolCall)
    {
        var name = toolCall.Function?.Name;
        var argsJson = toolCall.Function?.Arguments ?? "{}";

        if (name == "delegate_task")
        {
            return await HandleDelegateTaskAsync(argsJson);
        }

        var globalTool = _globalTools.FirstOrDefault(t => t.Name == name);
        if (globalTool != null) return await globalTool.ExecuteAsync(argsJson);

        return $"Error: Unknown tool '{name}'";
    }

    private async Task<string> HandleDelegateTaskAsync(string argsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            if (args == null ||
                !args.TryGetValue("projectId", out var projectId) ||
                !args.TryGetValue("roleId", out var roleId) ||
                !args.TryGetValue("instructions", out var instructions))
            {
                return "Error: projectId, roleId, and instructions are required.";
            }

            var rolesFile = Path.Combine(AppContext.BaseDirectory, "Data", "Roles", "roles.json");
            if (!File.Exists(rolesFile)) return "Error: No roles defined in the system.";

            var rolesJson = await File.ReadAllTextAsync(rolesFile);
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var roles = JsonSerializer.Deserialize<List<RoleDefinition>>(rolesJson, jsOptions);

            var role = roles?.FirstOrDefault(r => r.RoleId.Equals(roleId, StringComparison.OrdinalIgnoreCase));
            if (role == null) return $"Error: Role '{roleId}' not found.";

            // Instantiate SpecialistAgent
            var specialist = new SpecialistAgent(_globalTools, role.Title, role.SystemPrompt, _configuration, _wikiClient);

            _logger.LogInformation($"Manager delegating task to SpecialistAgent ({role.Title}) for project {projectId}.");

            var combinedInstructions = $"Project ID: {projectId}\nTask Instructions:\n{instructions}";

            // We need a unique session ID for the sub-agent
            var subSessionId = $"sub-{Guid.NewGuid():N}";

            var result = await _orchestrator.RunAgentLoopAsync(specialist, combinedInstructions, subSessionId, "ManagerAgent");

            // Instruct the orchestrator to terminate the manager agent loop
            return $"[TERMINATE_MANAGER_LOOP] Specialist output:\n{result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error delegating task.");
            return $"Error delegating task: {ex.Message}";
        }
    }

    private class RoleDefinition
    {
        public string RoleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
    }
}
