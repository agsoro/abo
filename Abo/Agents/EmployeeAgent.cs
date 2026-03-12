using System.Text.Json;
using System.Xml.Linq;
using Abo.Contracts.OpenAI;
using Abo.Core.Connectors;
using Abo.Tools;
using Abo.Tools.Connector;

namespace Abo.Agents;

public class EmployeeAgent : IAgent
{
    private readonly IEnumerable<IAboTool> _globalTools;
    private readonly string _dataDir;

    // State for the currently checked-out project
    private string? _currentProjectId;
    private IConnector? _currentConnector;
    private List<IAboTool> _connectorTools = new();
    private bool _isValidationTask;

    public string Name => "EmployeeAgent";
    public string Description => "The General Employee Agent. Takes a specific task from a running project and works on it. Uses a secure context connector to execute file/shell operations on the project's environment.";
    public bool RequiresCapableModel => true;
    public bool RequiresReviewModel => _isValidationTask;

    public EmployeeAgent(IEnumerable<IAboTool> globalTools)
    {
        _globalTools = globalTools;
        _dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
    }

    public string SystemPrompt =>
        "You are the Generic Employee (Role_Employee). Your goal is to work on tasks from running projects in the company.\n\n" +
        "### WORKFLOW:\n" +
        "1. **Find Work**: Use `list_projects` to see all active projects and their current step/state. Find a project that has work to do.\n" +
        "2. **Checkout Project**: Once you choose a project, you MUST use `checkout_project` providing its `projectId`. This securely binds your file/shell tools (the Connector) to that project's specific environment. DO NOT guess paths.\n" +
        "3. **Adopt Role & Announce**: Read the project's `info.md` and `notes.md` (if it exists) to understand the requirements, context, and previous step results. Instruct yourself to mentally 'slip into' the required Role. Important: You must ALWAYS provide a chat message to the CEO announcing that you are starting this task and which role you are taking. DO NOT wait for the CEO to acknowledge, but immediately proceed to execute further tools.\n" +
        "4. **Execute**: Use the connector tools (`read_file`, `write_file`, `list_dir`, `mkdir`, `git`, `dotnet`) to perform your work. All relative paths are automatically rooted in the checked-out project's directory.\n" +
        "5. **Complete**: When the task is done, use 'complete_task' to signal completion. You MUST supply 'resultNotes' detailing your executed work, outputs, and any context needed by the next Role. Important: You must ALWAYS provide a final chat message directly to the CEO, giving a status update on what you accomplished and a short explanation of *how and why* you made your decisions. You may then proceed to the next task if applicable.\n\n" +
        "### RULES:\n" +
        "- You cannot use file/system tools until you have checked out a project.\n" +
        "- Do not attempt to bypass the relative path confinement.";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();

        // 1. Global tools
        var allowedGlobalTools = new[] { "list_projects", "get_open_work", "get_system_time", "get_roles", "get_environments" };
        foreach (var tool in _globalTools.Where(t => allowedGlobalTools.Contains(t.Name)))
        {
            definitions.Add(CreateDef(tool));
        }

        // 2. Custom Agent lifecycle tools
        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "checkout_project",
                Description = "Checks out a running project by ID, binding your environment connector to it so you can use file/system tools.",
                Parameters = new { type = "object", properties = new { projectId = new { type = "string" } }, required = new[] { "projectId" } }
            }
        });

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "complete_task",
                Description = "Marks the current task in your checked-out project as completed and updates its status.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        nextStepId = new { type = "string", description = "Optional. The ID of the next BPMN step to advance this project to if you know it." },
                        resultNotes = new { type = "string", description = "A detailed summary of the parameters, context, or results generated during this step to store in notes.md for the next person/agent. (Required)" }
                    },
                    required = new[] { "resultNotes" }
                }
            }
        });

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "request_ceo_help",
                Description = "Stops work and asks the human CEO for help or clarification.",
                Parameters = new { type = "object", properties = new { message = new { type = "string" } }, required = new[] { "message" } }
            }
        });

        // 3. Connector tool signatures (always exposed, but restricted functionally if not checked out)
        // We instantiate dummy ones just to get schemas 
        var dummyEnv = new ConnectorEnvironment { Dir = "C:\\" };
        var dummyConn = new LocalWindowsConnector(dummyEnv);
        definitions.Add(CreateDef(new ReadFileTool(dummyConn)));
        definitions.Add(CreateDef(new WriteFileTool(dummyConn)));
        definitions.Add(CreateDef(new DeleteFileTool(dummyConn)));
        definitions.Add(CreateDef(new ListDirTool(dummyConn)));
        definitions.Add(CreateDef(new MkDirTool(dummyConn)));
        definitions.Add(CreateDef(new GitTool(dummyConn)));
        definitions.Add(CreateDef(new DotnetTool(dummyConn)));
        definitions.Add(CreateDef(new SearchRegexTool(dummyConn)));

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
        var args = toolCall.Function?.Arguments ?? "{}";

        // Handle Project Lifecycle
        if (name == "checkout_project") return await HandleCheckoutProjectAsync(args);
        if (name == "complete_task") return await HandleCompleteTaskAsync(args);
        if (name == "request_ceo_help") return HandleRequestCeoHelp(args);

        // Handle Global Tools
        var globalTool = _globalTools.FirstOrDefault(t => t.Name == name);
        if (globalTool != null) return await globalTool.ExecuteAsync(args);

        // Handle Connector Tools
        var connectorToolNames = new[] { "read_file", "write_file", "delete_file", "list_dir", "mkdir", "git", "dotnet", "search_regex" };
        if (connectorToolNames.Contains(name))
        {
            if (_currentConnector == null || string.IsNullOrEmpty(_currentProjectId))
            {
                return "Error: You must execute 'checkout_project' before using any file or system tools.";
            }

            var tool = _connectorTools.FirstOrDefault(t => t.Name == name);
            if (tool != null) return await tool.ExecuteAsync(args);
        }

        return $"Error: Unknown tool '{name}'";
    }

    private async Task<string> HandleCheckoutProjectAsync(string argsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            if (args == null || !args.TryGetValue("projectId", out var projectId)) return "Error: projectId required.";

            var activeProjectsFile = Path.Combine(_dataDir, "Projects", "active_projects.json");
            if (!File.Exists(activeProjectsFile)) return "Error: No active projects found.";

            var activeProjectsJson = await File.ReadAllTextAsync(activeProjectsFile);
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // We need a local class for deserialization
            var projects = JsonSerializer.Deserialize<List<ProjectRecord>>(activeProjectsJson, jsOptions);

            // Migration: ensure all entries have a Status
            if (projects != null)
            {
                foreach (var p in projects)
                {
                    if (string.IsNullOrWhiteSpace(p.Status))
                        p.Status = "running";
                }
            }

            var project = projects?.FirstOrDefault(p => p.Id == projectId);

            if (project == null) return $"Error: Project '{projectId}' not found in active projects.";
            if (string.IsNullOrWhiteSpace(project.EnvironmentName)) return $"Error: Project '{projectId}' does not have a configured environment.";

            // Resolve environment
            var envsFile = Path.Combine(_dataDir, "Environments", "environments.json");
            if (!File.Exists(envsFile)) return "Error: Environments config missing.";

            var envsJson = await File.ReadAllTextAsync(envsFile);
            var envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envsJson, jsOptions);
            var targetEnv = envs?.FirstOrDefault(e => e.Name.Equals(project.EnvironmentName, StringComparison.OrdinalIgnoreCase));

            if (targetEnv == null) return $"Error: Environment '{project.EnvironmentName}' not found in configuration.";

            // Initialize connection
            _currentProjectId = projectId;
            _currentConnector = new LocalWindowsConnector(targetEnv);

            _isValidationTask = project.CurrentStepId != null &&
                (project.CurrentStepId.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                 project.CurrentStepId.Contains("qa", StringComparison.OrdinalIgnoreCase) ||
                 project.CurrentStepId.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                 project.CurrentStepId.Contains("validation", StringComparison.OrdinalIgnoreCase));

            _connectorTools.Clear();
            _connectorTools.Add(new ReadFileTool(_currentConnector));
            _connectorTools.Add(new WriteFileTool(_currentConnector));
            _connectorTools.Add(new DeleteFileTool(_currentConnector));
            _connectorTools.Add(new ListDirTool(_currentConnector));
            _connectorTools.Add(new MkDirTool(_currentConnector));
            _connectorTools.Add(new GitTool(_currentConnector));
            _connectorTools.Add(new DotnetTool(_currentConnector));
            _connectorTools.Add(new SearchRegexTool(_currentConnector));

            return $"Successfully checked out project '{projectId}'. You are now bound to environment '{targetEnv.Name}' located at '{targetEnv.Dir}'. Your relative paths will root here.";
        }
        catch (Exception ex)
        {
            return $"Checkout error: {ex.Message}";
        }
    }

    /// <summary>
    /// Resolves the next actionable task step from a BPMN document.
    /// Automatically skips over gateways and intermediate events by following
    /// the single outgoing path (for exclusive gateways without a condition,
    /// or when only one outgoing flow exists).
    /// Returns null if the target is an endEvent (project is done),
    /// or the step ID of the next userTask / serviceTask / scriptTask.
    /// Returns "GATEWAY_REQUIRES_DECISION" if a gateway has multiple unresolvable paths.
    /// </summary>
    private static string? ResolveNextActionableStep(XDocument xdoc, string fromStepId, int maxHops = 10)
    {
        var current = fromStepId;

        for (int i = 0; i < maxHops; i++)
        {
            var outgoingFlows = xdoc.Descendants()
                .Where(e => e.Name.LocalName == "sequenceFlow" && e.Attribute("sourceRef")?.Value == current)
                .ToList();

            if (outgoingFlows.Count == 0)
                return null; // No outgoing flows → dead end, treat as completed

            if (outgoingFlows.Count > 1)
            {
                // Multiple paths: this is a decision gateway that requires explicit nextStepId
                // Check if the current node itself is an exclusive gateway
                var currentNode = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == current);
                if (currentNode != null && currentNode.Name.LocalName == "exclusiveGateway")
                    return "GATEWAY_REQUIRES_DECISION";

                // If current node is a task with multiple outgoing flows (unusual), also require decision
                return "GATEWAY_REQUIRES_DECISION";
            }

            // Exactly one outgoing flow → follow it
            var targetRef = outgoingFlows.First().Attribute("targetRef")?.Value;
            if (string.IsNullOrWhiteSpace(targetRef))
                return null;

            var targetNode = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == targetRef);
            if (targetNode == null)
                return null;

            var nodeType = targetNode.Name.LocalName;

            // If it's an endEvent → project is complete
            if (nodeType == "endEvent")
                return targetRef; // Return the endEvent ID so caller can detect completion

            // If it's an actionable task → return it
            if (nodeType == "userTask" || nodeType == "serviceTask" || nodeType == "scriptTask" || nodeType == "task")
                return targetRef;

            // If it's a gateway or intermediate event with a single outgoing flow → traverse through it
            if (nodeType == "exclusiveGateway" || nodeType == "parallelGateway" ||
                nodeType == "inclusiveGateway" || nodeType == "intermediateCatchEvent" ||
                nodeType == "intermediateThrowEvent")
            {
                current = targetRef; // Continue traversal from this gateway
                continue;
            }

            // Unknown node type → return it and let the caller handle
            return targetRef;
        }

        return null; // Max hops exceeded
    }

    /// <summary>
    /// Determines whether a given stepId represents a terminal/end state in the BPMN process.
    /// This covers:
    ///   1. The stepId is an actual endEvent node in the BPMN XML.
    ///   2. The stepId does not exist in the BPMN at all (orphaned/legacy step IDs like "Event_End").
    ///      These must be treated as completed because no further workflow exists.
    /// </summary>
    private static bool IsEndState(XDocument xdoc, string stepId)
    {
        var node = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == stepId);

        // Case 1: Node is explicitly an endEvent
        if (node != null && node.Name.LocalName == "endEvent")
            return true;

        // Case 2: Node does not exist in the BPMN at all (orphaned step ID)
        // This handles legacy/misspelled IDs like "Event_End" that were committed to
        // active_projects.json but never existed in the actual BPMN definition.
        if (node == null)
            return true;

        return false;
    }

    private async Task<string> HandleCompleteTaskAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentProjectId)) return "Error: No checked-out project to complete.";

        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            string? nextStepId = null;
            if (args != null) args.TryGetValue("nextStepId", out nextStepId);

            var activeProjectsFile = Path.Combine(_dataDir, "Projects", "active_projects.json");
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
            List<ProjectRecord>? projects = null;
            ProjectRecord? proj = null;

            if (File.Exists(activeProjectsFile))
            {
                var activeProjectsJson = await File.ReadAllTextAsync(activeProjectsFile);
                projects = JsonSerializer.Deserialize<List<ProjectRecord>>(activeProjectsJson, jsOptions);

                // Migration: ensure all entries have a Status
                if (projects != null)
                {
                    foreach (var p in projects)
                    {
                        if (string.IsNullOrWhiteSpace(p.Status))
                            p.Status = "running";
                    }
                }

                proj = projects?.FirstOrDefault(p => p.Id == _currentProjectId);
            }

            if (proj == null) return "Error: Could not locate active project record.";

            // Load the BPMN document once for all subsequent checks
            XDocument? bpmnXdoc = null;
            var bpmnFile = Path.Combine(_dataDir, "Processes", $"{proj.TypeId}.bpmn");
            if (File.Exists(bpmnFile))
            {
                try
                {
                    var xml = await File.ReadAllTextAsync(bpmnFile);
                    bpmnXdoc = XDocument.Parse(xml);
                }
                catch { /* Ignore XML parse errors */ }
            }

            // If nextStepId is not provided, dynamically resolve it from the BPMN definition
            // using the enhanced resolver that skips gateways automatically
            if (string.IsNullOrWhiteSpace(nextStepId) && bpmnXdoc != null)
            {
                var resolved = ResolveNextActionableStep(bpmnXdoc, proj.CurrentStepId);

                if (resolved == "GATEWAY_REQUIRES_DECISION")
                {
                    return "Error: The current step leads to a decision gateway with multiple paths. You MUST provide the 'nextStepId' parameter to indicate which path the process should take.";
                }

                nextStepId = resolved;
            }

            // Determine if the project has reached an end state.
            // An end state is reached when:
            //   a) nextStepId is null/empty (no further steps found in BPMN)
            //   b) nextStepId points to an actual endEvent node in the BPMN
            //   c) nextStepId references a node that does NOT exist in the BPMN
            //      (e.g. legacy IDs like "Event_End" that were erroneously stored in active_projects.json)
            bool reachedEndEvent = false;
            if (string.IsNullOrWhiteSpace(nextStepId))
            {
                reachedEndEvent = true;
            }
            else if (bpmnXdoc != null)
            {
                reachedEndEvent = IsEndState(bpmnXdoc, nextStepId);
            }

            var projectDir = Path.Combine(_dataDir, "Projects", _currentProjectId);

            // Handle Result Notes
            string? resultNotes = null;
            if (args != null && args.TryGetValue("resultNotes", out resultNotes) && !string.IsNullOrWhiteSpace(resultNotes))
            {
                var notesFile = Path.Combine(projectDir, "notes.md");
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
                var noteEntry = $"\n\n## Step Completed: {proj.CurrentStepId} ({timestamp})\n{resultNotes}\n---";
                if (!File.Exists(notesFile))
                {
                    await File.WriteAllTextAsync(notesFile, $"# Project Notes: {proj.Title}\nThis file contains a running log of executed steps and context passed between agents.");
                }
                await File.AppendAllTextAsync(notesFile, noteEntry);
            }

            var statusFile = Path.Combine(projectDir, "status.json");

            if (File.Exists(statusFile))
            {
                var statusJson = await File.ReadAllTextAsync(statusFile);
                var state = JsonSerializer.Deserialize<ProcessState>(statusJson, jsOptions);

                if (state != null)
                {
                    state.History ??= new List<ProcessStepHistory>();
                    state.History.Add(new ProcessStepHistory
                    {
                        StepId = proj.CurrentStepId,
                        CompletedAt = DateTime.UtcNow,
                        ResultNotes = resultNotes
                    });

                    state.Status = reachedEndEvent ? "Completed" : "Active";
                    if (!string.IsNullOrWhiteSpace(nextStepId)) state.CurrentStepId = nextStepId;
                    state.LastUpdated = DateTime.UtcNow;

                    await File.WriteAllTextAsync(statusFile, JsonSerializer.Serialize(state, jsOptions));
                }
            }

            // Update active_projects.json
            if (projects != null && proj != null)
            {
                if (reachedEndEvent)
                {
                    // Project is done: remove from active list
                    projects.Remove(proj);
                }
                else
                {
                    proj.CurrentStepId = nextStepId!;
                    proj.Status = "running";
                }
                await File.WriteAllTextAsync(activeProjectsFile, JsonSerializer.Serialize(projects, jsOptions));
            }

            var oldProj = _currentProjectId;
            _currentProjectId = null;
            _currentConnector = null;
            _connectorTools.Clear();

            if (reachedEndEvent)
            {
                return $"Success. Task completed for project '{oldProj}'. You have been un-bound from it. The project has reached an endEvent and is now fully completed and will be removed from the active projects list. Please explicitly inform the CEO about the successful completion and closure of the project in your next message.";
            }

            return $"Success. Task completed for project '{oldProj}'. Advanced to next step: '{nextStepId}'. You have been un-bound from it.";
        }
        catch (Exception ex)
        {
            return $"Error completing task: {ex.Message}";
        }
    }

    private string HandleRequestCeoHelp(string argsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            if (args != null && args.TryGetValue("message", out var message))
            {
                return $"CEO HELP REQUESTED: {message}";
            }
            return "CEO help requested, but no message provided.";
        }
        catch (Exception ex)
        {
            return $"Error parsing help message: {ex.Message}";
        }
    }

    private class ProjectRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string CurrentStepId { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string Status { get; set; } = "running";
    }

    private class ProcessState
    {
        public string ProjectId { get; set; } = string.Empty;
        public string CurrentStepId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public List<ProcessStepHistory> History { get; set; } = new();
    }

    private class ProcessStepHistory
    {
        public string StepId { get; set; } = string.Empty;
        public DateTime CompletedAt { get; set; }
        public string? ResultNotes { get; set; }
    }
}
