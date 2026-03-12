using System.Text.Json;
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

    public string Name => "EmployeeAgent";
    public string Description => "The General Employee Agent. Takes a specific task from a running project and works on it. Uses a secure context connector to execute file/shell operations on the project's environment.";
    public bool RequiresCapableModel => true;

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
        "3. **Adopt Role**: Read the project's `info.md` or its BPMN process to understand the requirements and what Role is needed for the current step. Instruct yourself to mentally 'slip into' the required Role (e.g., 'I am now the Senior Developer...').\n" +
        "4. **Execute**: Use the connector tools (`read_file`, `write_file`, `list_dir`, `mkdir`, `git`, `dotnet`) to perform your work. All relative paths are automatically rooted in the checked-out project's directory.\n" +
        "5. **Complete**: When the task is done, use `complete_task` to signal completion and mark your state as done. If you get stuck, use `request_ceo_help` to ask for human assistance.\n\n" +
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
                Parameters = new { type = "object", properties = new { nextStepId = new { type = "string", description = "Optional. The ID of the next BPMN step to advance this project to if you know it." } } }
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
        var connectorToolNames = new[] { "read_file", "write_file", "delete_file", "list_dir", "mkdir", "git", "dotnet" };
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

            _connectorTools.Clear();
            _connectorTools.Add(new ReadFileTool(_currentConnector));
            _connectorTools.Add(new WriteFileTool(_currentConnector));
            _connectorTools.Add(new DeleteFileTool(_currentConnector));
            _connectorTools.Add(new ListDirTool(_currentConnector));
            _connectorTools.Add(new MkDirTool(_currentConnector));
            _connectorTools.Add(new GitTool(_currentConnector));
            _connectorTools.Add(new DotnetTool(_currentConnector));

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

            var projectDir = Path.Combine(_dataDir, "Projects", _currentProjectId);
            var statusFile = Path.Combine(projectDir, "status.json");

            if (File.Exists(statusFile))
            {
                var statusJson = await File.ReadAllTextAsync(statusFile);
                // Update file, simplistic approach
                var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
                var statusDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(statusJson, jsOptions);

                var dict = new Dictionary<string, object>();
                if (statusDict != null)
                {
                    foreach (var kvp in statusDict) dict[kvp.Key] = kvp.Value;
                }

                dict["Status"] = "Completed";
                if (!string.IsNullOrWhiteSpace(nextStepId)) dict["CurrentStepId"] = nextStepId;
                dict["LastUpdated"] = DateTime.UtcNow;

                await File.WriteAllTextAsync(statusFile, JsonSerializer.Serialize(dict, jsOptions));
            }

            // Update active_projects.json
            var activeProjectsFile = Path.Combine(_dataDir, "Projects", "active_projects.json");
            if (File.Exists(activeProjectsFile))
            {
                var activeProjectsJson = await File.ReadAllTextAsync(activeProjectsFile);
                var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
                var projects = JsonSerializer.Deserialize<List<ProjectRecord>>(activeProjectsJson, jsOptions);
                if (projects != null)
                {
                    var proj = projects.FirstOrDefault(p => p.Id == _currentProjectId);
                    if (proj != null && !string.IsNullOrWhiteSpace(nextStepId))
                    {
                        proj.CurrentStepId = nextStepId;
                        await File.WriteAllTextAsync(activeProjectsFile, JsonSerializer.Serialize(projects, jsOptions));
                    }
                }
            }

            var oldProj = _currentProjectId;
            _currentProjectId = null;
            _currentConnector = null;
            _connectorTools.Clear();

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
        public string StatusLink { get; set; } = string.Empty;
    }
}
