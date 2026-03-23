using System.Text.Json;
using System.Xml.Linq;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Abo.Integrations.GitHub;
using Abo.Tools;
using Microsoft.Extensions.Configuration;

namespace Abo.Tools;

public class GetOpenWorkTool : IAboTool
{
    private readonly string _processesDirectory;
    private readonly IConfiguration _config;

    public GetOpenWorkTool(IConfiguration config)
    {
        _config = config;
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        _processesDirectory = Path.Combine(dataDir, "Processes");
    }

    public string Name => "get_open_work";
    public string Description => "Analyzes all active issues/issues and extracts actionable tasks. Returns a structured list of open work, revealing the expected role and state based on the BPMN flow.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "Environments", "environments.json");
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var envs = new List<ConnectorEnvironment>();
            
            if (File.Exists(environmentsFile))
            {
                var envJson = await File.ReadAllTextAsync(environmentsFile);
                envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envJson, jsOptions) ?? new();
            }

            var activeIssues = new List<IssueRecord>();

            if (envs.Any())
            {
                foreach (var env in envs.Where(e => e.IssueTracker != null))
                {
                    IIssueTrackerConnector? tracker = null;
                    if (env.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = _config["Integrations:GitHub:Token"];
                        tracker = new GitHubIssueTrackerConnector(env.IssueTracker, token, env.Name);
                    }
                    else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                    {
                        tracker = new FileSystemIssueTrackerConnector(env.Name);
                    }

                    if (tracker != null)
                    {
                        var issues = await tracker.ListIssuesAsync(state: "open");
                        activeIssues.AddRange(issues);
                    }
                }
            }


            if (!activeIssues.Any())
            {
                return "No open issue work found.";
            }

            var output = new System.Text.StringBuilder();
            output.AppendLine("# Open Work Items\n");

            foreach (var issue in activeIssues)
            {
                var typeId = ExtractLabelValue(issue.Labels, "type") ?? "Unknown";
                var stepId = ExtractLabelValue(issue.Labels, "step") ?? "Unknown";
                var role = ExtractLabelValue(issue.Labels, "role") ?? "Unknown";
                var envName = ExtractLabelValue(issue.Labels, "env") ?? "Unknown";
                var projRef = ExtractLabelValue(issue.Labels, "ref") ?? issue.Id;

                var bpmnFile = Path.Combine(_processesDirectory, $"{typeId}.bpmn");
                string nodeName = stepId;
                string nodeType = "Unknown Type";
                string status = "Unknown State";

                // Resolve against BPMN if file exists
                if (File.Exists(bpmnFile))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(bpmnFile);
                        var xdoc = XDocument.Parse(xml);

                        // Find node by ID across all elements in the process
                        var node = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == stepId);

                        if (node != null)
                        {
                            nodeName = node.Attribute("name")?.Value ?? stepId;
                            nodeType = node.Name.LocalName;

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

                output.AppendLine($"### Issue: {issue.Title} (Ref: `{projRef}` | Issue: `{issue.Id}`)");
                output.AppendLine($"- **Environment**: `{envName}`");
                output.AppendLine($"- **Issue Status**: `{issue.State}`");
                output.AppendLine($"- **Current Step**: {nodeName} (`{stepId}`)");
                if (!string.IsNullOrWhiteSpace(role))
                    output.AppendLine($"- **Required Role**: `{role}`");
                output.AppendLine($"- **BPMN Node Type**: `{nodeType}`");
                output.AppendLine($"- **State**: {status}");
                output.AppendLine($"- **Action**: Run `checkout_task {{\\\"issueId\\\": \\\"{issue.Id}\\\"}}` to pick up this work.");
                output.AppendLine();
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading open work: {ex.Message}";
        }
    }

    private string? ExtractLabelValue(IEnumerable<string> labels, string key)
    {
        var prefix = key + ": ";
        var match = labels.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?.Substring(prefix.Length).Trim();
    }
}
