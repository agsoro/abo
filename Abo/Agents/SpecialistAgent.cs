using System.Text.Json;
using System.Xml.Linq;
using Abo.Contracts.Models;
using Abo.Contracts.OpenAI;
using Abo.Core.Connectors;
using Abo.Tools;
using Abo.Tools.Connector;

namespace Abo.Agents;

public class SpecialistAgent : IAgent
{
    private readonly IEnumerable<IAboTool> _globalTools;
    private readonly string _dataDir;
    private readonly string _roleTitle;
    private readonly string _rolePrompt;

    // State for the currently checked-out project
    private string? _currentProjectId;
    private IConnector? _currentConnector;
    private List<IAboTool> _connectorTools = new();
    private bool _isValidationTask;

    public string Name => "SpecialistAgent";
    public string Description => "A dynamically instantiated agent taking on a specific expert role to complete a delegated task.";
    public bool RequiresCapableModel => true;
    public bool RequiresReviewModel => _isValidationTask;

    public SpecialistAgent(IEnumerable<IAboTool> globalTools, string roleTitle, string rolePrompt)
    {
        _globalTools = globalTools;
        _roleTitle = roleTitle;
        _rolePrompt = rolePrompt;
        _dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
    }

    public string SystemPrompt =>
        $"You are `{_roleTitle}`. Your role description and instructions:\n" +
        $"------\n{_rolePrompt}\n------\n\n" +
        "### INSTRUCTIONS FOR YOUR TASK:\n" +
        "You have been assigned a specific task by the ManagerAgent. Read your instructions carefully.\n" +
        "### WORKFLOW:\n" +
        "1. **Checkout Project**: You MUST use `checkout_task` providing the `projectId` from your instructions. This securely binds your file/shell tools (the Connector) to that project's specific environment. DO NOT guess paths.\n" +
        "2. **Execute**: Use the connector tools (`read_file`, `write_file`, `list_dir`, `mkdir`, `git`, `dotnet`, `python`, `http_get`) to perform your work. All relative paths are automatically rooted in the checked-out project's directory.\n" +
        "3. **Complete**: When the task is done, use 'complete_task' to signal completion. You MUST supply 'resultNotes' detailing your executed work, outputs, and any context needed by the next Role. If you decide the next step, you must supply the explicit 'nextStep' object containing 'id', 'name', and 'role'.\n\n" +
        "### RULES:\n" +
        "- You cannot use file/system tools until you have checked out a project.\n" +
        "- Do not attempt to bypass the relative path confinement.";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();

        // 1. Global tools
        var allowedGlobalTools = new[] { "get_environments" };
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
                Name = "checkout_task",
                Description = "Checks out the next task from a project by ID, binding your environment connector to it so you can use file/system tools.",
                Parameters = new { type = "object", properties = new { projectId = new { type = "string" } }, required = new[] { "projectId" } }
            }
        });

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "complete_task",
                Description = "Marks the current task in your checked-out project as completed and updates its status. This will also terminate your session.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        nextStep = new
                        {
                            type = "object",
                            description = "Optional. The exact step object (including id, name, and role) to jump to next. Omitting this implies the process should advance natively or has reached an end state.",
                            properties = new
                            {
                                id = new { type = "string" },
                                name = new { type = "string" },
                                role = new { type = "string" }
                            },
                            required = new[] { "id", "name", "role" }
                        },
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

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "take_notes",
                Description = "Stores temporary notes, remarks, or intermediate findings during your task. These are securely saved to the project's remarks file.",
                Parameters = new { type = "object", properties = new { note = new { type = "string" } }, required = new[] { "note" } }
            }
        });

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "read_notes",
                Description = "Reads the temporary notes, remarks, or intermediate findings stored for the current project.",
                Parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            }
        });

        // 3. Connector tool signatures (always exposed, but restricted functionally if not checked out)
        var dummyEnv = new ConnectorEnvironment { Dir = "C:\\" };
        var dummyConn = new LocalWindowsConnector(dummyEnv);
        definitions.Add(CreateDef(new ReadFileTool(dummyConn)));
        definitions.Add(CreateDef(new WriteFileTool(dummyConn)));
        definitions.Add(CreateDef(new DeleteFileTool(dummyConn)));
        definitions.Add(CreateDef(new ListDirTool(dummyConn)));
        definitions.Add(CreateDef(new MkDirTool(dummyConn)));
        definitions.Add(CreateDef(new GitTool(dummyConn)));
        definitions.Add(CreateDef(new DotnetTool(dummyConn)));
        definitions.Add(CreateDef(new PythonTool(dummyConn)));
        definitions.Add(CreateDef(new SearchRegexTool(dummyConn)));
        definitions.Add(CreateDef(new HttpGetTool(dummyConn)));   // ABO-XXXX: http_get Tool

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
        if (name == "checkout_task") return await HandleCheckoutTaskAsync(args);
        if (name == "complete_task") return await HandleCompleteTaskAsync(args);
        if (name == "request_ceo_help") return HandleRequestCeoHelp(args);
        if (name == "take_notes") return await HandleTakeNotesAsync(args);
        if (name == "read_notes") return await HandleReadNotesAsync(args);

        // Handle Global Tools
        var globalTool = _globalTools.FirstOrDefault(t => t.Name == name);
        if (globalTool != null) return await globalTool.ExecuteAsync(args);

        // Handle Connector Tools (inkl. http_get – ABO-XXXX)
        var connectorToolNames = new[]
        {
            "read_file", "write_file", "delete_file", "list_dir", "mkdir",
            "git", "dotnet", "python", "search_regex", "http_get"
        };
        if (connectorToolNames.Contains(name))
        {
            if (_currentConnector == null || string.IsNullOrEmpty(_currentProjectId))
            {
                return "Error: You must execute 'checkout_task' before using any file or system tools.";
            }

            var tool = _connectorTools.FirstOrDefault(t => t.Name == name);
            if (tool != null) return await tool.ExecuteAsync(args);
        }

        return $"Error: Unknown tool '{name}'";
    }

    private async Task<string> HandleCheckoutTaskAsync(string argsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            if (args == null || !args.TryGetValue("projectId", out var projectId)) return "Error: projectId required.";

            var activeProjectsFile = Path.Combine(_dataDir, "Projects", "active_projects.json");
            if (!File.Exists(activeProjectsFile)) return "Error: No active projects found.";

            var activeProjectsJson = await File.ReadAllTextAsync(activeProjectsFile);
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var projects = JsonSerializer.Deserialize<List<ProjectRecord>>(activeProjectsJson, jsOptions);

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

            var envsFile = Path.Combine(_dataDir, "Environments", "environments.json");
            if (!File.Exists(envsFile)) return "Error: Environments config missing.";

            var envsJson = await File.ReadAllTextAsync(envsFile);
            var envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envsJson, jsOptions);
            var targetEnv = envs?.FirstOrDefault(e => e.Name.Equals(project.EnvironmentName, StringComparison.OrdinalIgnoreCase));

            if (targetEnv == null) return $"Error: Environment '{project.EnvironmentName}' not found in configuration.";

            _currentProjectId = projectId;
            _currentConnector = new LocalWindowsConnector(targetEnv);

            _isValidationTask = _roleTitle.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                _roleTitle.Contains("qa", StringComparison.OrdinalIgnoreCase) ||
                _roleTitle.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                _roleTitle.Contains("validation", StringComparison.OrdinalIgnoreCase);

            _connectorTools.Clear();
            _connectorTools.Add(new ReadFileTool(_currentConnector));
            _connectorTools.Add(new WriteFileTool(_currentConnector));
            _connectorTools.Add(new DeleteFileTool(_currentConnector));
            _connectorTools.Add(new ListDirTool(_currentConnector));
            _connectorTools.Add(new MkDirTool(_currentConnector));
            _connectorTools.Add(new GitTool(_currentConnector));
            _connectorTools.Add(new DotnetTool(_currentConnector));
            _connectorTools.Add(new PythonTool(_currentConnector));
            _connectorTools.Add(new SearchRegexTool(_currentConnector));
            _connectorTools.Add(new HttpGetTool(_currentConnector));  // ABO-XXXX: http_get Tool

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
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            if (args == null || !args.TryGetValue("resultNotes", out var resultNotesElement))
            {
                return "Error: 'resultNotes' is required to complete the task.";
            }
            var resultNotes = resultNotesElement.GetString();

            ProcessStepInfo? nextStepInfo = null;
            if (args.TryGetValue("nextStep", out var nextStepObj) && nextStepObj.ValueKind == JsonValueKind.Object)
            {
                nextStepInfo = new ProcessStepInfo
                {
                    StepId = nextStepObj.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                    StepName = nextStepObj.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "",
                    RequiredRole = nextStepObj.TryGetProperty("role", out var roleProp) ? roleProp.GetString() ?? "" : ""
                };
            }

            var activeProjectsFile = Path.Combine(_dataDir, "Projects", "active_projects.json");
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
            List<ProjectRecord>? projects = null;
            ProjectRecord? proj = null;

            if (File.Exists(activeProjectsFile))
            {
                var activeProjectsJson = await File.ReadAllTextAsync(activeProjectsFile);
                projects = JsonSerializer.Deserialize<List<ProjectRecord>>(activeProjectsJson, jsOptions);

                if (projects != null)
                {
                    foreach (var p in projects)
                    {
                        if (string.IsNullOrWhiteSpace(p.Status)) p.Status = "running";
                    }
                }

                proj = projects?.FirstOrDefault(p => p.Id == _currentProjectId);
            }

            if (proj == null) return "Error: Could not locate active project record.";

            // If not provided explicitly, try to read the workflow to find the next logical step
            if (nextStepInfo == null)
            {
                var bpmnFile = Path.Combine(_dataDir, "Processes", $"{proj.TypeId}.bpmn");
                if (File.Exists(bpmnFile))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(bpmnFile);
                        var bpmnXdoc = XDocument.Parse(xml);

                        var resolvedId = ResolveNextActionableStep(bpmnXdoc, proj.CurrentStep.StepId);

                        if (resolvedId == "GATEWAY_REQUIRES_DECISION")
                        {
                            return "Error: The current step leads to a decision gateway with multiple paths. You MUST provide the 'nextStep' object explicitly so the engine knows which route to take.";
                        }

                        if (!string.IsNullOrWhiteSpace(resolvedId))
                        {
                            var targetNode = bpmnXdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == resolvedId);
                            var stepName = targetNode?.Attribute("name")?.Value ?? resolvedId;
                            var requiredRole = targetNode?.Attributes().FirstOrDefault(a => a.Name.LocalName == "assignee")?.Value ?? string.Empty;

                            if (string.IsNullOrWhiteSpace(requiredRole))
                            {
                                var docs = targetNode?.Descendants().FirstOrDefault(e => e.Name.LocalName == "documentation")?.Value;
                                if (!string.IsNullOrWhiteSpace(docs))
                                {
                                    var roleMatch = System.Text.RegularExpressions.Regex.Match(docs, @"Role:\s*(Role_[^\s\r\n]+)");
                                    if (roleMatch.Success) requiredRole = roleMatch.Groups[1].Value;
                                }
                            }

                            nextStepInfo = new ProcessStepInfo
                            {
                                StepId = resolvedId,
                                StepName = stepName,
                                RequiredRole = requiredRole
                            };
                        }
                    }
                    catch { }
                }
            }

            if (nextStepInfo == null)
            {
                return "Error: Could not automatically determine the next step. You must supply 'nextStep' with id, name, and role based on the BPMN definition.";
            }

            bool reachedEndEvent = nextStepInfo.StepName.Contains("abgeschlossen", StringComparison.OrdinalIgnoreCase) ||
                                   nextStepInfo.StepName.Contains("done", StringComparison.OrdinalIgnoreCase) ||
                                   nextStepInfo.StepId.Contains("EndEvent", StringComparison.OrdinalIgnoreCase) ||
                                   string.IsNullOrWhiteSpace(nextStepInfo.RequiredRole);

            var currentStepInfo = proj.CurrentStep;
            var projectDir = Path.Combine(_dataDir, "Projects", _currentProjectId);

            if (!string.IsNullOrWhiteSpace(resultNotes))
            {
                var notesFile = Path.Combine(projectDir, "notes.md");
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
                var noteEntry = $"\n\n## Step Completed: {proj.CurrentStep.StepId} ({timestamp})\n{resultNotes}\n---";
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
                        Step = currentStepInfo,
                        CompletedAt = DateTime.UtcNow,
                        ResultNotes = resultNotes
                    });

                    state.Status = reachedEndEvent ? "Completed" : "Active";
                    if (nextStepInfo != null) state.CurrentStep = nextStepInfo;
                    state.LastUpdated = DateTime.UtcNow;

                    await File.WriteAllTextAsync(statusFile, JsonSerializer.Serialize(state, jsOptions));
                }
            }

            if (projects != null && proj != null)
            {
                if (reachedEndEvent)
                {
                    projects.Remove(proj);
                }
                else
                {
                    proj.CurrentStep = nextStepInfo!;
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
                return $"Success. Task completed for project '{oldProj}'. The project has reached an endEvent and is now fully completed.";
            }

            return $"Success. Task completed for project '{oldProj}'. Advanced to next step: '{nextStepInfo?.StepId}'.";
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

    private async Task<string> HandleTakeNotesAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentProjectId)) return "Error: You must check out a task before taking notes.";

        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            if (args == null || !args.TryGetValue("note", out var note))
            {
                return "Error: 'note' is required.";
            }

            var projectDir = Path.Combine(_dataDir, "Projects", _currentProjectId);
            if (!Directory.Exists(projectDir))
            {
                Directory.CreateDirectory(projectDir);
            }

            var remarksFile = Path.Combine(projectDir, "remarks.md");
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
            var noteEntry = $"\n\n### Remark ({timestamp})\n{note}\n---";

            if (!File.Exists(remarksFile))
            {
                await File.WriteAllTextAsync(remarksFile, $"# Project Remarks\nTemporary notes and intermediate findings stored by specialists during tasks.");
            }
            await File.AppendAllTextAsync(remarksFile, noteEntry);

            return "Note successfully saved to remarks.md.";
        }
        catch (Exception ex)
        {
            return $"Error saving note: {ex.Message}";
        }
    }

    private async Task<string> HandleReadNotesAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentProjectId)) return "Error: You must check out a task before reading notes.";

        try
        {
            var projectDir = Path.Combine(_dataDir, "Projects", _currentProjectId);
            var remarksFile = Path.Combine(projectDir, "remarks.md");

            if (!File.Exists(remarksFile))
            {
                return "No remarks or notes found for this project.";
            }

            return await File.ReadAllTextAsync(remarksFile);
        }
        catch (Exception ex)
        {
            return $"Error reading notes: {ex.Message}";
        }
    }

    private string? ResolveNextActionableStep(XDocument xdoc, string fromStepId, int maxHops = 10)
    {
        var current = fromStepId;

        for (int i = 0; i < maxHops; i++)
        {
            var outgoingFlows = xdoc.Descendants()
                .Where(e => e.Name.LocalName == "sequenceFlow" && e.Attribute("sourceRef")?.Value == current)
                .ToList();

            if (outgoingFlows.Count == 0) return null;

            if (outgoingFlows.Count > 1) return "GATEWAY_REQUIRES_DECISION";

            var targetRef = outgoingFlows.First().Attribute("targetRef")?.Value;
            if (string.IsNullOrWhiteSpace(targetRef)) return null;

            var targetNode = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == targetRef);
            if (targetNode == null) return null;

            var nodeType = targetNode.Name.LocalName;

            if (nodeType == "endEvent") return targetRef;

            if (nodeType == "userTask" || nodeType == "serviceTask" || nodeType == "scriptTask" || nodeType == "task") return targetRef;

            if (nodeType == "exclusiveGateway" || nodeType == "parallelGateway" ||
                nodeType == "inclusiveGateway" || nodeType == "intermediateCatchEvent" ||
                nodeType == "intermediateThrowEvent")
            {
                current = targetRef;
                continue;
            }

            return targetRef;
        }

        return null;
    }

    private class ProjectRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public ProcessStepInfo CurrentStep { get; set; } = new();
        public string EnvironmentName { get; set; } = string.Empty;
        public string Status { get; set; } = "running";
    }

    private class ProcessState
    {
        public string ProjectId { get; set; } = string.Empty;
        public ProcessStepInfo CurrentStep { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; }
        public List<ProcessStepHistory> History { get; set; } = new();
    }

    private class ProcessStepHistory
    {
        public ProcessStepInfo Step { get; set; } = new();
        public DateTime CompletedAt { get; set; }
        public string? ResultNotes { get; set; }
    }
}
