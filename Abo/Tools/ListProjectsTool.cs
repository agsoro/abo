using System.Text.Json;
using Abo.Tools;

namespace Abo.Tools;

public class ListProjectsTool : IAboTool
{
    private readonly string _activeProjectsFile;

    public ListProjectsTool()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var projectsDir = Path.Combine(dataDir, "Projects");
        _activeProjectsFile = Path.Combine(projectsDir, "active_projects.json");
    }

    public string Name => "list_projects";
    public string Description => "Lists all currently active projects, subprojects, their process types, and current state. This provides full visibility into running processes.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        if (!File.Exists(_activeProjectsFile))
        {
            return "No active projects found.";
        }

        try
        {
            var existingJson = await File.ReadAllTextAsync(_activeProjectsFile);
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var activeProjects = JsonSerializer.Deserialize<List<ActiveProjectRecord>>(existingJson, jsOptions);

            if (activeProjects == null || !activeProjects.Any())
            {
                return "No active projects found.";
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Active Projects Hierarchy");

            // Root projects
            var roots = activeProjects.Where(p => string.IsNullOrWhiteSpace(p.ParentId)).ToList();
            foreach (var root in roots)
            {
                AppendProject(output, root, activeProjects, 0);
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading active projects: {ex.Message}";
        }
    }

    private void AppendProject(System.Text.StringBuilder output, ActiveProjectRecord project, List<ActiveProjectRecord> allProjects, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);
        output.AppendLine($"{indent}- **[{project.Id}] {project.Title}**");
        output.AppendLine($"{indent}  - Type: `{project.TypeId}`");
        output.AppendLine($"{indent}  - Step: `{project.CurrentStepId}`");
        if (!string.IsNullOrWhiteSpace(project.EnvironmentName))
        {
            output.AppendLine($"{indent}  - Environment: `{project.EnvironmentName}`");
        }

        var children = allProjects.Where(p => p.ParentId == project.Id).ToList();
        foreach (var child in children)
        {
            AppendProject(output, child, allProjects, indentLevel + 1);
        }
    }

    private class ActiveProjectRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public string CurrentStepId { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = string.Empty;
    }
}
