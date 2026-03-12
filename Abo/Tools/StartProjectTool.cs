using System.Text.Json;
using Abo.Tools;

namespace Abo.Tools;

public class StartProjectTool : IAboTool
{
    private readonly string _projectsDirectory;
    private readonly string _processesDirectory;
    private readonly string _activeProjectsFile;

    public StartProjectTool()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        _projectsDirectory = Path.Combine(dataDir, "Projects");
        _processesDirectory = Path.Combine(dataDir, "Processes");
        _activeProjectsFile = Path.Combine(_projectsDirectory, "active_projects.json");

        if (!Directory.Exists(_projectsDirectory)) Directory.CreateDirectory(_projectsDirectory);
    }

    public string Name => "start_project";
    public string Description => "Starts a new project instance based on an existing BPMN process ID (type). This initializes the project directory, info.md, tracking state, and adds it to the active projects list.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            projectId = new { type = "string", description = "The unique numeric or alphanumeric ID for the instantiated project (e.g., 1001)." },
            title = new { type = "string", description = "A concise title for the project." },
            typeId = new { type = "string", description = "The ID of the process flow this project runs on (e.g., Type_Dev_Sprint). MUST be an existing process." },
            info = new { type = "string", description = "The markdown content describing the goals, context, and initial parameters of the project." },
            parentId = new { type = "string", description = "Optional. The ID of the parent project if this is a subproject." },
            initialStepId = new { type = "string", description = "The exact ID of the Starting Node / Item in the BPMN process to initialize the state tracker with (e.g., StartEvent_1 or Step_Analyze)." },
            environmentName = new { type = "string", description = "The name of the environment to supply for the project (e.g. 'thingsboard'). Execute get_environments to find valid names." }
        },
        required = new[] { "projectId", "title", "typeId", "info", "initialStepId", "environmentName" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        StartProjectArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<StartProjectArgs>(argumentsJson, jsOptions);
        }
        catch
        {
            return "Failed to parse arguments.";
        }

        if (args == null || string.IsNullOrWhiteSpace(args.ProjectId) || string.IsNullOrWhiteSpace(args.TypeId) || string.IsNullOrWhiteSpace(args.EnvironmentName))
            return "Invalid arguments provided. Ensure projectId, typeId, and environmentName are not empty.";

        var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "Environments", "environments.json");
        if (File.Exists(environmentsFile))
        {
            var envJson = await File.ReadAllTextAsync(environmentsFile);
            var envs = JsonSerializer.Deserialize<List<Abo.Core.Connectors.ConnectorEnvironment>>(envJson, jsOptions);
            if (envs != null && !envs.Any(e => e.Name.Equals(args.EnvironmentName, StringComparison.OrdinalIgnoreCase)))
            {
                return $"Error: Environment '{args.EnvironmentName}' not found. Please use get_environments to see available environments.";
            }
        }

        var processFile = Path.Combine(_processesDirectory, $"{args.TypeId}.bpmn");
        if (!File.Exists(processFile))
        {
            return $"Error: The process type '{args.TypeId}' does not exist. Create the process first.";
        }

        try
        {
            // 1. Create project folder
            var projectFolder = Path.Combine(_projectsDirectory, args.ProjectId);
            if (Directory.Exists(projectFolder))
            {
                return $"Error: Project ID '{args.ProjectId}' already exists.";
            }
            Directory.CreateDirectory(projectFolder);

            // 2. Write info.md
            var infoPath = Path.Combine(projectFolder, "info.md");
            await File.WriteAllTextAsync(infoPath, $"# {args.Title}\n\n**ID:** {args.ProjectId}\n**Type:** {args.TypeId}\n**Parent:** {args.ParentId ?? "None"}\n**Environment:** {args.EnvironmentName}\n\n## Context\n{args.Info}");

            // 3. Write state tracker (status.json)
            var state = new ProcessState
            {
                ProjectId = args.ProjectId,
                CurrentStepId = args.InitialStepId,
                Status = "Active",
                LastUpdated = DateTime.UtcNow,
                History = new()
            };
            var statePath = Path.Combine(projectFolder, "status.json");
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));

            // 4. Update central active_projects.json
            List<ActiveProjectRecord> activeProjects = new();
            if (File.Exists(_activeProjectsFile))
            {
                var existingJson = await File.ReadAllTextAsync(_activeProjectsFile);
                activeProjects = JsonSerializer.Deserialize<List<ActiveProjectRecord>>(existingJson, jsOptions) ?? new();
                // Migration: ensure existing entries have a Status
                foreach (var p in activeProjects)
                {
                    if (string.IsNullOrWhiteSpace(p.Status))
                        p.Status = "running";
                }
            }

            activeProjects.Add(new ActiveProjectRecord
            {
                Id = args.ProjectId,
                Title = args.Title,
                TypeId = args.TypeId,
                ParentId = args.ParentId,
                CurrentStepId = args.InitialStepId,
                EnvironmentName = args.EnvironmentName,
                Status = "running"
            });

            await File.WriteAllTextAsync(_activeProjectsFile, JsonSerializer.Serialize(activeProjects, new JsonSerializerOptions { WriteIndented = true }));

            return $"Successfully started project '{args.ProjectId}' ({args.Title}). State initialized at step '{args.InitialStepId}'.";
        }
        catch (Exception ex)
        {
            return $"Error starting project: {ex.Message}";
        }
    }

    private class StartProjectArgs
    {
        public string ProjectId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string Info { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string InitialStepId { get; set; } = string.Empty;
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

    private class ActiveProjectRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string CurrentStepId { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
        public string Status { get; set; } = "running";
    }
}
