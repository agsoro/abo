using System.Text.Json;
using System.Xml.Linq;
using Abo.Contracts.Models;
using Abo.Contracts.OpenAI;
using Abo.Core;
using Abo.Core.Connectors;
using Abo.Tools;
using Abo.Tools.Connector;
using Abo.Integrations.GitHub;
using Abo.Integrations.XpectoLive;
using Microsoft.Extensions.Configuration;

namespace Abo.Agents;

public class SpecialistAgent : IAgent
{
    private readonly IEnumerable<IAboTool> _globalTools;
    private readonly string _dataDir;
    private readonly string _roleTitle;
    private readonly string _rolePrompt;
    private readonly List<string> _allowedTools;
    private readonly string? _issueTrackerToken;
    private readonly IXpectoLiveWikiClient _wikiClient;
    private readonly IConfiguration _config;

    // State for the currently checked-out issue
    private string? _currentIssueId;
    private IssueRecord? _currentIssue;
    private IWorkspaceConnector? _currentWorkspace;
    private IIssueTrackerConnector? _currentIssueTracker;
    private IWikiConnector? _currentWiki;
    private List<IAboTool> _connectorTools = new();
    private bool _isValidationTask;

    public string Name => "SpecialistAgent";
    public string Description => "A dynamically instantiated agent taking on a specific expert role to complete a delegated task.";
    public bool RequiresCapableModel => true;
    public bool RequiresReviewModel => _isValidationTask;

    public SpecialistAgent(IEnumerable<IAboTool> globalTools, string roleTitle, string systemPrompt, List<string> allowedTools, IConfiguration configuration, string issueId, IXpectoLiveWikiClient? wikiClient = null)
    {
        _globalTools = globalTools;
        _roleTitle = roleTitle;
        _rolePrompt = systemPrompt;
        _allowedTools = allowedTools;
        _config = configuration;
        _currentIssueId = issueId;
        _issueTrackerToken = configuration["Integrations:GitHub:Token"];
        _wikiClient = wikiClient!;
        _dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
    }

    public string SystemPrompt =>
        $"You are `{_roleTitle}`. Your role description and instructions:\n" +
        $"------\n{_rolePrompt}\n------\n\n" +
        "### INSTRUCTIONS FOR YOUR TASK:\n" +
        "You have been assigned a specific task by the ManagerAgent. Read your instructions carefully.\n" +
        "### WORKFLOW:\n" +
        "1. **Get Issue**: read the issue via `issueId` (Issue ID) from your instructions.\n" +
        "2. **Execute**: Use the tools to perform your work. All relative paths are automatically rooted in the checked-out issue's directory.\n" +
        "3. **Complete**: When the task is done, use 'complete_task' to signal completion. You MUST supply 'resultNotes' detailing your executed work, outputs, and any context needed by the next Role. (These notes will be automatically added as a comment to the issue, so DO NOT use 'add_issue_comment' to duplicate this information). If there are multiple possible next steps (e.g., a decision gateway), you must supply a 'keyword' matching the condition to take.\n\n" +
        "### RULES:\n" +
        "- Do not attempt to bypass the relative path confinement.";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();

        // 1. Global tools
        // No global tools currently allowed for SpecialistAgent

        // 2. Custom Agent lifecycle tools

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "complete_task",
                Description = "Marks the current task in your checked-out issue as completed and updates its status. This will also terminate your session.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        keyword = new
                        {
                            type = "string",
                            description = "Optional. If the current step has multiple possible next steps, provide a keyword matching the condition name of the path to take."
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
        // 3. Connector tool signatures (now fully mounted before execution via InitializeWorkspaceAsync)
        foreach (var tool in _connectorTools)
        {
            if (_allowedTools == null || !_allowedTools.Any() || _allowedTools.Contains(tool.Name))
            {
                definitions.Add(CreateDef(tool));
            }
        }

        return definitions;
    }

    private ToolDefinition CreateDef(IAboTool tool) => new ToolDefinition
    {
        Type = "function",
        Function = new FunctionDefinition { Name = tool.Name, Description = tool.Description, Parameters = tool.ParametersSchema }
    };

    public async Task<string> HandleToolCallAsync(ToolCall toolCall)
    {
        var name = toolCall.Function?.Name;
        var args = toolCall.Function?.Arguments ?? "{}";

        if (name == "complete_task") return await HandleCompleteTaskAsync(args);
        if (name == "request_ceo_help") return HandleRequestCeoHelp(args);

        var globalTool = _globalTools.FirstOrDefault(t => t.Name == name);
        if (globalTool != null) return await globalTool.ExecuteAsync(args);

        var connectorToolNames = new[] { "read_file", "write_file", "delete_file", "list_dir", "mkdir", "git", "dotnet", "python", "search_regex", "http_get", "list_issues", "get_issue", "create_issue", "add_issue_comment", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki" };
        if (connectorToolNames.Contains(name))
        {
            // Specifically exclude checking if workspace is null if it's an issue/wiki tool that doesn't need it?
            if (_currentWorkspace == null || string.IsNullOrEmpty(_currentIssueId))
            {
                return "Error: Workspace not bound structurally. Internal system flow failure during execution boot.";
            }

            var tool = _connectorTools.FirstOrDefault(t => t.Name == name);
            if (tool != null)
            {
                if (_allowedTools != null && _allowedTools.Any() && !string.IsNullOrEmpty(name) && !_allowedTools.Contains(name))
                {
                    return $"Error: Tool '{name}' is restricted and cannot be run by your current role.";
                }
                return await tool.ExecuteAsync(args);
            }
        }

        return $"Error: Unknown tool '{name}'";
    }

    public async Task<string> InitializeWorkspaceAsync()
    {
        try
        {
            var issueId = _currentIssueId;
            if (string.IsNullOrEmpty(issueId)) return "Error: issueId was not provided.";

            var environmentsFile = Path.Combine(_dataDir, "Environments", "environments.json");
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var envs = new List<ConnectorEnvironment>();
            if (File.Exists(environmentsFile))
            {
                var envJson = await File.ReadAllTextAsync(environmentsFile);
                envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envJson, jsOptions) ?? new();
            }

            IssueRecord? targetIssue = null;
            IIssueTrackerConnector? matchingTracker = null;
            ConnectorEnvironment? targetEnv = null;

            foreach (var env in envs.Where(e => e.IssueTracker != null))
            {
                IIssueTrackerConnector? tracker = null;
                if (env.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                {
                    tracker = new GitHubIssueTrackerConnector(env.IssueTracker, _issueTrackerToken, env.Name);
                }
                else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                {
                    tracker = new FileSystemIssueTrackerConnector(env.Name);
                }

                if (tracker != null)
                {
                    try
                    {
                        var issue = await tracker.GetIssueAsync(issueId);
                        if (issue != null)
                        {
                            targetIssue = issue;
                            matchingTracker = tracker;
                            targetEnv = env;
                            break;
                        }
                    }
                    catch { /* Ignore non-existent */ }
                }
            }

            if (targetIssue == null || matchingTracker == null || targetEnv == null) return $"Error: Issue/Issue '{issueId}' not found across any configured issue trackers.";

            _currentIssueId = issueId;
            _currentIssue = targetIssue;
            _currentIssueTracker = matchingTracker;
            _currentWorkspace = new LocalWorkspaceConnector(targetEnv);

            if (targetEnv.Wiki != null)
            {
                if (targetEnv.Wiki.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                {
                    _currentWiki = new FileSystemWikiConnector(targetEnv);
                }
                else if (targetEnv.Wiki.Type.Equals("xpectolive", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(targetEnv.Wiki.RootPath))
                {
                    _currentWiki = new XpectoLiveWikiConnector(_wikiClient, targetEnv.Wiki.RootPath);
                }
                else if (targetEnv.Wiki.Type.Equals("github", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(targetEnv.Wiki.RootPath))
                {
                    var parts = targetEnv.Wiki.RootPath.Split('/');
                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(_issueTrackerToken))
                    {
                        _currentWiki = new GitHubWikiConnector(targetEnv, _issueTrackerToken, parts[0], parts[1]);
                    }
                }
            }

            _isValidationTask = _roleTitle.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                _roleTitle.Contains("qa", StringComparison.OrdinalIgnoreCase) ||
                _roleTitle.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                _roleTitle.Contains("validation", StringComparison.OrdinalIgnoreCase);

            _connectorTools.Clear();
            _connectorTools.Add(new ReadFileTool(_currentWorkspace));
            _connectorTools.Add(new WriteFileTool(_currentWorkspace));
            _connectorTools.Add(new DeleteFileTool(_currentWorkspace));
            _connectorTools.Add(new ListDirTool(_currentWorkspace));
            _connectorTools.Add(new MkDirTool(_currentWorkspace));
            _connectorTools.Add(new GitTool(_currentWorkspace));
            _connectorTools.Add(new DotnetTool(_currentWorkspace));
            _connectorTools.Add(new PythonTool(_currentWorkspace));
            _connectorTools.Add(new SearchRegexTool(_currentWorkspace));
            _connectorTools.Add(new HttpGetTool(_currentWorkspace));

            _connectorTools.Add(new ListIssuesTool(_currentIssueTracker));
            _connectorTools.Add(new GetIssueTool(_currentIssueTracker));
            _connectorTools.Add(new CreateIssueTool(_currentIssueTracker));
            _connectorTools.Add(new AddIssueCommentTool(_currentIssueTracker));

            if (_currentWiki != null)
            {
                _connectorTools.Add(new GetWikiPageTool(_currentWiki));
                _connectorTools.Add(new CreateWikiPageTool(_currentWiki));
                _connectorTools.Add(new UpdateWikiPageTool(_currentWiki));
                _connectorTools.Add(new SearchWikiTool(_currentWiki));
            }

            return $"Successfully checked out issue/issue '{issueId}'. You are now bound to environment '{targetEnv.Name}' located at '{targetEnv.Dir}'. Your relative paths will root here.";
        }
        catch (Exception ex)
        {
            return $"Checkout error: {ex.Message}";
        }
    }

    private async Task<string> HandleCompleteTaskAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentIssueId) || _currentIssueTracker == null || _currentIssue == null) return "Error: No checked-out issue to complete.";

        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            if (args == null || !args.TryGetValue("resultNotes", out var resultNotesElement)) return "Error: 'resultNotes' is required.";
            var resultNotes = resultNotesElement.GetString();

            string? keyword = null;
            if (args.TryGetValue("keyword", out var keywordElement) && keywordElement.ValueKind == JsonValueKind.String)
            {
                keyword = keywordElement.GetString();
            }

            var currentStepId = Abo.Core.WorkflowEngine.ResolveStepIdFallback(_currentIssue);

            ProcessStepInfo? nextStepInfo = null;

            if (!string.IsNullOrWhiteSpace(currentStepId))
            {
                var transitions = Abo.Core.WorkflowEngine.GetTransitions(currentStepId);

                if (transitions.Count > 1)
                {
                    if (string.IsNullOrWhiteSpace(keyword))
                    {
                        var options = string.Join(", ", transitions.Select(t => $"'{t.ConditionName}'"));
                        return $"Error: The current step leads to a decision gateway with multiple paths. You MUST provide the 'keyword' parameter matching one of these condition names to proceed: {options}";
                    }

                    var matchedTransition = transitions.FirstOrDefault(t => t.ConditionName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || keyword.Contains(t.ConditionName, StringComparison.OrdinalIgnoreCase));

                    if (matchedTransition == null)
                    {
                        var options = string.Join(", ", transitions.Select(t => $"'{t.ConditionName}'"));
                        return $"Error: The provided keyword '{keyword}' did not match any routing conditions. Valid expected condition matches are: {options}";
                    }

                    var stepInfo = Abo.Core.WorkflowEngine.GetStepInfo(matchedTransition.NextStepId);
                    if (stepInfo != null)
                    {
                        nextStepInfo = stepInfo;
                        matchedTransition.ApplyState?.Invoke(_currentIssue);
                    }
                }
                else if (transitions.Count == 1)
                {
                    var resolvedId = transitions.First().NextStepId;
                    var stepInfo = Abo.Core.WorkflowEngine.GetStepInfo(resolvedId);
                    if (stepInfo != null)
                    {
                        nextStepInfo = stepInfo;
                        transitions.First().ApplyState?.Invoke(_currentIssue);
                    }
                }
            }

            if (nextStepInfo == null) return "Error: Could not automatically determine the next workflow step.";

            bool reachedEndEvent = string.Equals(nextStepInfo.StepId, "invalid", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(nextStepInfo.StepId, "done", StringComparison.OrdinalIgnoreCase) ||
                                   string.IsNullOrWhiteSpace(nextStepInfo.RequiredRole);

            if (!string.IsNullOrWhiteSpace(resultNotes))
            {
                await _currentIssueTracker.AddIssueCommentAsync(_currentIssueId, resultNotes);
            }

            var updatedLabels = _currentIssue.Labels.Where(l => !l.StartsWith("role: ") && !l.StartsWith("env: ")).ToList();
            if (!reachedEndEvent)
            {
                await _currentIssueTracker.UpdateIssueAsync(_currentIssueId, state: "open", labels: updatedLabels.ToArray(), project: _currentIssue.Project, stepId: nextStepInfo.StepId);
            }
            else
            {
                await _currentIssueTracker.UpdateIssueAsync(_currentIssueId, state: "closed", labels: updatedLabels.ToArray(), project: _currentIssue.Project);
            }

            _currentIssueId = null;
            _currentWorkspace = null;
            _currentIssueTracker = null;
            _currentIssue = null;
            _connectorTools.Clear();

            // Return the sentinel prefix followed by the resultNotes so the Orchestrator can
            // immediately surface the notes to the user without an extra LLM round-trip.
            // The Orchestrator detects AgentSentinels.CompleteTaskResult and short-circuits the loop.
            return $"{AgentSentinels.CompleteTaskResult}{resultNotes}";
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
            if (args != null && args.TryGetValue("message", out var message)) return $"CEO HELP REQUESTED: {message}";
            return "CEO help requested, but no message provided.";
        }
        catch (Exception ex) { return $"Error parsing help message: {ex.Message}"; }
    }


}
