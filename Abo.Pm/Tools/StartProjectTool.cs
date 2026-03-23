using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Abo.Contracts.Models;
using Abo.Core.Connectors;
using Abo.Integrations.GitHub;
using Abo.Tools;
using Microsoft.Extensions.Configuration;

namespace Abo.Tools;

public class StartProjectTool : IAboTool
{
    private readonly string _processesDirectory;
    private readonly IConfiguration _config;

    public StartProjectTool(IConfiguration config)
    {
        _config = config;
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        _processesDirectory = Path.Combine(dataDir, "Processes");
    }

    public string Name => "start_project";
    public string Description => "Starts a new project instance based on an existing BPMN process ID (type). This creates an issue in the environment's configured tracker.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            projectId = new { type = "string", description = "The unique numeric or alphanumeric ID for the instantiated project (used as an internal reference)." },
            title = new { type = "string", description = "A concise title for the project." },
            typeId = new { type = "string", description = "The ID of the process flow this project runs on (e.g., Type_Dev_Sprint). MUST be an existing process." },
            info = new { type = "string", description = "The markdown content describing the goals, context, and initial parameters of the project." },
            parentId = new { type = "string", description = "Optional. The ID of the parent project if this is a subproject." },
            initialStepId = new { type = "string", description = "The exact ID of the Starting Node / Item in the BPMN process to initialize the tracking state with." },
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
        ConnectorEnvironment? targetEnv = null;
        if (File.Exists(environmentsFile))
        {
            var envJson = await File.ReadAllTextAsync(environmentsFile);
            var envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envJson, jsOptions);
            targetEnv = envs?.FirstOrDefault(e => e.Name.Equals(args.EnvironmentName, StringComparison.OrdinalIgnoreCase));
            if (targetEnv == null)
            {
                return $"Error: Environment '{args.EnvironmentName}' not found. Please use get_environments to see available environments.";
            }
        }

        if (targetEnv == null) return "Error: Failed to load target environment config.";

        var processFile = Path.Combine(_processesDirectory, $"{args.TypeId}.bpmn");
        if (!File.Exists(processFile))
        {
            return $"Error: The process type '{args.TypeId}' does not exist. Create the process first.";
        }

        try
        {
            // Parse XML to get Step details
            var xdoc = XDocument.Load(processFile);
            var stepNode = xdoc.Descendants().FirstOrDefault(e => e.Attribute("id")?.Value == args.InitialStepId);

            if (stepNode == null)
            {
                return $"Error: The initial step '{args.InitialStepId}' does not exist in the BPMN process.";
            }

            var stepName = stepNode.Attribute("name")?.Value ?? args.InitialStepId;
            var requiredRole = stepNode.Attributes().FirstOrDefault(a => a.Name.LocalName == "assignee")?.Value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(requiredRole))
            {
                var docs = stepNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "documentation")?.Value;
                if (!string.IsNullOrWhiteSpace(docs))
                {
                    var roleMatch = Regex.Match(docs, @"Role:\s*(Role_[^\s\r\n]+)");
                    if (roleMatch.Success)
                    {
                        requiredRole = roleMatch.Groups[1].Value;
                    }
                }
            }

            var initialStepInfo = new ProcessStepInfo
            {
                StepId = args.InitialStepId,
                StepName = stepName,
                RequiredRole = requiredRole
            };

            // Setup Issue Tracker
            IIssueTrackerConnector? tracker = null;
            if (targetEnv.IssueTracker != null)
            {
                if (targetEnv.IssueTracker.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                {
                    var token = _config["Integrations:GitHub:Token"];
                    tracker = new GitHubIssueTrackerConnector(targetEnv.IssueTracker, token, targetEnv.Name);
                }
                else if (targetEnv.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                {
                    tracker = new FileSystemIssueTrackerConnector(targetEnv.Name);
                }
            }

            if (tracker == null)
            {
                return $"Error: Environment '{args.EnvironmentName}' has no configured IssueTracker.";
            }

            // Write info.md equivalent
            var bodyArgs = $"**Internal Reference ID:** {args.ProjectId}\n**Type:** {args.TypeId}\n**Parent:** {args.ParentId ?? "None"}\n**Environment:** {args.EnvironmentName}\n\n## Context\n{args.Info}";

            // Map State to Labels
            var labels = new List<string>
            {
                $"env: {args.EnvironmentName}",
                $"step: {args.InitialStepId}",
                $"role: {requiredRole}",
                $"ref: {args.ProjectId}"
            };

            if (!string.IsNullOrWhiteSpace(args.ParentId)) labels.Add($"parent: {args.ParentId}");

            var issue = await tracker.CreateIssueAsync(args.Title, bodyArgs, args.TypeId, "", labels.ToArray());

            return $"Successfully started project '{args.ProjectId}' ({args.Title}). Tracking via Issue ID: {issue.Id}. State initialized at step '{args.InitialStepId}'.";
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
}
