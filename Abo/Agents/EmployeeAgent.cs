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
                proj = projects?.FirstOrDefault(p => p.Id == _currentProjectId);
            }

            if (proj == null) return "Error: Could not locate active project record.";

            // If nextStepId is not provided, dynamically resolve it from the BPMN definition
            if (string.IsNullOrWhiteSpace(nextStepId))
            {
                var bpmnFile = Path.Combine(_dataDir, "Processes", $"{proj.TypeId}.bpmn");
                if (File.Exists(bpmnFile))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(bpmnFile);
                        var xdoc = XDocument.Parse(xml);

                        // Find all outgoing sequence flows from the current step
                        var seqFlows = xdoc.Descendants().Where(e => e.Name.LocalName == "sequenceFlow" && e.Attribute("sourceRef")?.Value == proj.CurrentStepId).ToList();

                        if (seqFlows.Count == 1)
                        {
                            var targetRef = seqFlows.First().Attribute("targetRef")?.Value;
                            if (!string.IsNullOrWhiteSpace(targetRef))
                            {
                                nextStepId = targetRef;
                            }
                        }
                        else if (seqFlows.Count > 1)
                        {
                            return "Error: The current step has multiple outgoing paths (decisions or gateways). You MUST provide the 'nextStepId' parameter to indicate which path the process should take.";
                        }
                    }
                    catch
                    {
                        // Ignore XML parse errors and fall back to manual supply / stuck state
                    }
                }
            }

            // Check if nextStepId is an endEvent (either provided or resolved)
            bool reachedEndEvent = false;
            var bpmnFileCheck = Path.Combine(_dataDir, "Processes", $"{proj.TypeId}.bpmn");
            if (File.Exists(bpmnFileCheck) && !string.IsNullOrWhiteSpace(nextStepId))
            {
                try
                {
                    var xml = await File.ReadAllTextAsync(bpmnFileCheck);
                    var xdoc = XDocument.Parse(xml);
                    var targetNode = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == nextStepId);
                    if (targetNode != null && targetNode.Name.LocalName == "endEvent")
                    {
                        reachedEndEvent = true;
                    }
                }
                catch { }
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

                    state.Status = (string.IsNullOrWhiteSpace(nextStepId) || reachedEndEvent) ? "Completed" : "Active";
                    if (!string.IsNullOrWhiteSpace(nextStepId)) state.CurrentStepId = nextStepId;
                    state.LastUpdated = DateTime.UtcNow;

                    await File.WriteAllTextAsync(statusFile, JsonSerializer.Serialize(state, jsOptions));
                }
            }

            // Update active_projects.json
            if (projects != null && proj != null)
            {
                if (reachedEndEvent || string.IsNullOrWhiteSpace(nextStepId))
                {
                    projects.Remove(proj);
                }
                else
                {
                    proj.CurrentStepId = nextStepId;
                }
                await File.WriteAllTextAsync(activeProjectsFile, JsonSerializer.Serialize(projects, jsOptions));
            }

            var oldProj = _currentProjectId;
            _currentProjectId = null;
            _currentConnector = null;
            _connectorTools.Clear();

            if (reachedEndEvent || string.IsNullOrWhiteSpace(nextStepId))
            {
                return $"Success. Task completed for project '{oldProj}'. You have been un-bound from it. The project has reached an endEvent and is now fully completed and will be removed from the active projects list. Please explicitly inform the CEO about the successful completion and closure of the project in your next message.";
            }

            return $"Success. Task completed for project '{oldProj}'. You have been un-bound from it.";
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
