using System.Text.Json;
using System.Xml.Linq;
using Abo.Tools;

namespace Abo.Tools;

public class ListProjectsTool : IAboTool
{
    private readonly string _activeProjectsFile;
    private readonly string _processesDirectory;

    public ListProjectsTool()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var projectsDir = Path.Combine(dataDir, "Projects");
        _activeProjectsFile = Path.Combine(projectsDir, "active_projects.json");
        _processesDirectory = Path.Combine(dataDir, "Processes");
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

            // Migration: ensure all entries have a Status
            foreach (var p in activeProjects)
            {
                if (string.IsNullOrWhiteSpace(p.Status))
                    p.Status = "running";
            }

            // Migration: remove projects whose CurrentStepId points to a non-existent or end-event
            // node in their BPMN definition. This cleans up orphaned projects that were stuck
            // at IDs like "Event_End" which never existed in the BPMN.
            var toRemove = new List<ActiveProjectRecord>();
            foreach (var p in activeProjects)
            {
                var bpmnFile = Path.Combine(_processesDirectory, $"{p.TypeId}.bpmn");
                if (File.Exists(bpmnFile))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(bpmnFile);
                        var xdoc = XDocument.Parse(xml);
                        var node = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == p.CurrentStepId);

                        // Flag for removal if: node doesn't exist in BPMN OR node is an endEvent
                        if (node == null || node.Name.LocalName == "endEvent")
                        {
                            toRemove.Add(p);
                        }
                    }
                    catch { /* Ignore BPMN parse errors for this project */ }
                }
            }

            bool changed = false;
            if (toRemove.Any())
            {
                foreach (var p in toRemove)
                    activeProjects.Remove(p);

                // Persist the cleaned-up list
                await File.WriteAllTextAsync(_activeProjectsFile,
                    System.Text.Json.JsonSerializer.Serialize(activeProjects, new JsonSerializerOptions { WriteIndented = true }));
                changed = true;
            }

            if (!activeProjects.Any())
            {
                return "No active projects found." + (changed ? " (Orphaned/completed projects were automatically cleaned up.)" : "");
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
        output.AppendLine($"{indent}  - Status: `{project.Status}`");
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
        public string Status { get; set; } = "running";
    }
}
