using Abo.Contracts.OpenAI;
using Abo.Tools;

namespace Abo.Agents;

public class PmoAgent : IAgent
{
    private readonly IEnumerable<IAboTool> _tools;

    public string Name => "PmoAgent";
    public string Description => "The Project Management Office (PMO) Lead Agent. Responsible for designing processes (BPMN), updating them, instantiating new project directories based on them, and listing active projects. Handles creating processes and tracking the life cycle of running projects.";
    public bool RequiresCapableModel => true;
    public bool RequiresReviewModel => false;

    public PmoAgent(IEnumerable<IAboTool> tools)
    {
        _tools = tools;
    }

    public string SystemPrompt =>
        "You are the PMO Lead (Role_PMO_Lead), the Architect of the company's operational foundation. You report to the CEO (Human).\n\n" +
        "### CORE CAPABILITIES:\n" +
        "1. **BPMN Generation**: Create and update BPMN process flows using `create_process` and `update_process`.\n" +
        "2. **Project Governance**: Instantiate flows into running projects using `start_project` and track them via `list_projects`.\n" +
        "3. **Role Management**: Store and reference AI agent roles using `upsert_role` and `get_roles`.\n\n" +
        "### STRICT RULES:\n" +
        "1. **BPMN Construction**: You MUST embed the exact XML representation of the process schema into the `bpmnXml` parameter. Ensure valid BPMN 2.0 structure.\n" +
        "2. **ID Mapping**: EVERY SINGLE ITEM (nodes, events, tasks, gateways, sequence flows) in your BPMN MUST HAVE a strict and explicit unique string ID (e.g. `Step_ReviewCode`, `Gateway_IsApproved`). Subprojects/project instances use these to identify the current step.\n" +
        "3. **Role Creation**: Whenever you define a new process that requires roles, you MUST use `get_roles` to see existing ones. If a role is missing or needs refinement, you MUST use `upsert_role` to auto-create and define its system prompt. The role ID must match what you put in the BPMN.\n" +
        "4. **PDCA Execution**: You use Plan -> Do -> Check -> Act to perform goals (Create Plan -> Draft BPMN/Roles -> Show CEO text summary -> Act and Save using tools).\n" +
        "5. **No Direct Execution**: You are the PMO designer. When you create a task in a BPMN flow for a 'QA' agent, you DO NOT do the QA yourself. The instantiated flow will trigger the respective agent.\n" +
        "6. **Process Visualization**: Inform the user they can view the created processes in the web UI (e.g., `/processes/index.html`).\n" +
        "7. **Process Reuse First – NEVER create a new process unless explicitly told to do so or no suitable process exists**: Before calling `create_process`, you MUST first call `list_projects` to inspect already-used process types. Evaluate whether an existing process type (typeId) can be reused for the new project via `start_project`. Only call `create_process` if: (a) the user explicitly requests a new process, OR (b) you have confirmed that no suitable existing process type covers the required workflow. Always prefer reuse over creation.\n\n" +
        "Use your process and role management tools wisely!";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();
        var allowedToolNames = new[] { "create_process", "update_process", "start_project", "list_projects", "get_open_work", "upsert_role", "get_roles", "get_system_time" };

        foreach (var tool in _tools.Where(t => allowedToolNames.Contains(t.Name)))
        {
            definitions.Add(new ToolDefinition
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = tool.ParametersSchema
                }
            });
        }
        return definitions;
    }

    public async Task<string> HandleToolCallAsync(ToolCall toolCall)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == toolCall.Function?.Name);
        if (tool != null)
        {
            return await tool.ExecuteAsync(toolCall.Function?.Arguments ?? "{}");
        }

        return $"Error: Unknown tool '{toolCall.Function?.Name}'";
    }
}
