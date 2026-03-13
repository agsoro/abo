using System.Text.Json;
using System.Xml.Linq;
using Abo.Contracts.Models;
using Abo.Tools;

namespace Abo.Tools;

public class GetOpenWorkTool : IAboTool
{
    private readonly string _projectsDirectory;
    private readonly string _processesDirectory;
    private readonly string _activeProjectsFile;

    public GetOpenWorkTool()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        _projectsDirectory = Path.Combine(dataDir, "Projects");
        _processesDirectory = Path.Combine(dataDir, "Processes");
        _activeProjectsFile = Path.Combine(_projectsDirectory, "active_projects.json");
    }

    public string Name => "get_open_work";
    public string Description => "Analyzes all active projects and extracts actionable tasks. Returns a structured list of open work, revealing the expected role and state based on the BPMN flow.";

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
            return "No active projects or open work found.";
        }

        try
        {
            var existingJson = await File.ReadAllTextAsync(_activeProjectsFile);
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var activeProjects = JsonSerializer.Deserialize<List<ActiveProjectRecord>>(existingJson, jsOptions);

            if (activeProjects == null || !activeProjects.Any())
            {
                return "No open project work found.";
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
                        var node = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == p.CurrentStep.StepId);

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
                return "No open project work found." + (changed ? " (Orphaned/completed projects were automatically cleaned up.)" : "");
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Open Work Items\n");

            foreach (var project in activeProjects)
            {
                var bpmnFile = Path.Combine(_processesDirectory, $"{project.TypeId}.bpmn");
                string nodeName = "Unknown Node";
                string nodeType = "Unknown Type";
                string status = "Unknown State";

                // Resolve against BPMN if file exists
                if (File.Exists(bpmnFile))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(bpmnFile);
                        var xdoc = XDocument.Parse(xml);

                        // BPMN 2.0 Namespace
                        XNamespace bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

                        // Find node by ID across all elements in the process
                        var node = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == project.CurrentStep.StepId);

                        if (node != null)
                        {
                            nodeName = node.Attribute("name")?.Value ?? project.CurrentStep.StepId;
                            nodeType = node.Name.LocalName; // e.g., userTask, scriptTask, startEvent

                            status = nodeType switch
                            {
                                "userTask" => "Ready (Waiting on Human/Agent Action)",
                                "serviceTask" => "Ready (Waiting on Service Execution)",
                                "scriptTask" => "Ready (Waiting on Script Execution)",
                                "task" => "Ready for work",
                                "startEvent" => "Newly Initialized",
                                "endEvent" => "Completed",
                                "intermediateCatchEvent" => "Waiting for Event/Subprocess",
                                "exclusiveGateway" => "Pending Decision",
                                "parallelGateway" => "Pending Divergence/Convergence",
                                _ => "State Undetermined"
                            };
                        }
                    }
                    catch
                    {
                        status = "Error Parsing Context";
                    }
                }

                output.AppendLine($"### Project: {project.Title} (ID: `{project.Id}`)");
                output.AppendLine($"- **Environment**: `{project.EnvironmentName}`");
                output.AppendLine($"- **Project Status**: `{project.Status}`");
                output.AppendLine($"- **Current Step**: {nodeName} (`{project.CurrentStep.StepId}`)");
                if (!string.IsNullOrWhiteSpace(project.CurrentStep.RequiredRole))
                    output.AppendLine($"- **Required Role**: `{project.CurrentStep.RequiredRole}`");
                output.AppendLine($"- **BPMN Node Type**: `{nodeType}`");
                output.AppendLine($"- **State**: {status}");
                output.AppendLine();
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading open work: {ex.Message}";
        }
    }

    private class ActiveProjectRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string TypeId { get; set; } = string.Empty;
        public ProcessStepInfo CurrentStep { get; set; } = new();
        public string EnvironmentName { get; set; } = string.Empty;
        public string Status { get; set; } = "running";
    }
}
