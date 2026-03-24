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
        "You are the ManagerAgent (Project Lead). Your goal is to oversee running issues and assign tasks to the correct specialists.\n\n" +
        "### WORKFLOW:\n" +
        "1. **Find Work**: Use `get_open_work` to see all active issues, their current step, and status. Find a issue that has work to do.\n" +
        "2. **Delegate Task**: Once you know the issue, use `delegate_task` to assign the work to a SpecialistAgent. You must provide the `issueId`.\n" +
        "3. **Completion**: The `delegate_task` tool will synchronously execute the specialist. Calling this tool will terminate your current manager assignment, since you have successfully handed the work off.\n\n" +
        "### RULES:\n" +
        "- You must use `delegate_task` to get the actual work done.\n" +
        "- **PRIORITY RULE**: Process all `open` issues first. When no `open` issues remain, pick the in-progress issue closest to completion: prefer `review` over `check` over `work` over `planned`. The `get_open_work` tool returns issues sorted by priority — pick the first actionable one.";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();

        var allowedGlobalTools = new[] { "list_issues", "get_open_work" };
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
                        issueId = new { type = "string", description = "The ID of the issue the specialist should work on." }
                    },
                    required = new[] { "issueId" }
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
                !args.TryGetValue("issueId", out var issueId))
            {
                return "Error: issueId is required.";
            }

            var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "Environments", "environments.json");
            var envs = new List<Abo.Core.Connectors.ConnectorEnvironment>();
            if (File.Exists(environmentsFile))
            {
                var envJson = await File.ReadAllTextAsync(environmentsFile);
                var jsOpt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                envs = JsonSerializer.Deserialize<List<Abo.Core.Connectors.ConnectorEnvironment>>(envJson, jsOpt) ?? new();
            }

            Abo.Contracts.Models.IssueRecord? targetIssue = null;
            foreach (var env in envs.Where(e => e.IssueTracker != null))
            {
                Abo.Core.Connectors.IIssueTrackerConnector? tracker = null;
                if (env.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                {
                    tracker = new Abo.Integrations.GitHub.GitHubIssueTrackerConnector(env.IssueTracker, _configuration["Integrations:GitHub:Token"], env.Name);
                }
                else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                {
                    tracker = new Abo.Core.Connectors.FileSystemIssueTrackerConnector(env.Name);
                }

                if (tracker != null)
                {
                    try
                    {
                        var issue = await tracker.GetIssueAsync(issueId);
                        if (issue != null)
                        {
                            targetIssue = issue;
                            break;
                        }
                    }
                    catch { /* Ignore */ }
                }
            }

            if (targetIssue == null) return $"Error: Issue '{issueId}' not found.";

            var stepId = Abo.Core.WorkflowEngine.ResolveStepIdFallback(targetIssue);

            var stepInfo = Abo.Core.WorkflowEngine.GetStepInfo(stepId);
            if (stepInfo == null || string.IsNullOrWhiteSpace(stepInfo.RequiredRole)) return $"Error: Could not determine RequiredRole for step '{stepId}'.";

            var roleId = stepInfo.RequiredRole;

            var roles = Abo.Core.Core.AvailableRoles.AllRoles;

            var role = roles?.FirstOrDefault(r => r.RoleId.Equals(roleId, StringComparison.OrdinalIgnoreCase));
            if (role == null) return $"Error: Role '{roleId}' not found.";

            // Instantiate SpecialistAgent
            var specialist = new SpecialistAgent(_globalTools, role.Title, role.SystemPrompt, role.AllowedTools, _configuration, issueId, _wikiClient);
            var initResult = await specialist.InitializeWorkspaceAsync();
            if (initResult.StartsWith("Error")) return $"Task delegation failed during environment setup: {initResult}";

            _logger.LogInformation($"Manager delegating task to SpecialistAgent ({role.Title}) for issue {issueId}.");

            var initialMessage = $"Issue ID: {issueId}";

            // We need a unique session ID for the sub-agent
            var subSessionId = $"sub-{Guid.NewGuid():N}";

            var result = await _orchestrator.RunAgentLoopAsync(specialist, initialMessage, subSessionId, "ManagerAgent", issueId: issueId);

            // Instruct the orchestrator to terminate the manager agent loop
            return $"[TERMINATE_MANAGER_LOOP] Specialist output:\n{result}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error delegating task.");
            return $"Error delegating task: {ex.Message}";
        }
    }
}
