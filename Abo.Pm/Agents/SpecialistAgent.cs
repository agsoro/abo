using System.Text.Json;
using System.Xml.Linq;
using Abo.Contracts.Models;
using Abo.Contracts.OpenAI;
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

    public SpecialistAgent(IEnumerable<IAboTool> globalTools, string roleTitle, string rolePrompt, List<string> allowedTools, IConfiguration config, IXpectoLiveWikiClient? wikiClient = null)
    {
        _globalTools = globalTools;
        _roleTitle = roleTitle;
        _rolePrompt = rolePrompt;
        _allowedTools = allowedTools ?? new();
        _config = config;
        _issueTrackerToken = config["Integrations:GitHub:Token"];
        _wikiClient = wikiClient!;
        _dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
    }

    public string SystemPrompt =>
        $"You are `{_roleTitle}`. Your role description and instructions:\n" +
        $"------\n{_rolePrompt}\n------\n\n" +
        "### INSTRUCTIONS FOR YOUR TASK:\n" +
        "You have been assigned a specific task by the ManagerAgent. Read your instructions carefully.\n" +
        "### WORKFLOW:\n" +
        "1. **Checkout Issue**: You MUST use `checkout_task` providing the `issueId` (Issue ID) from your instructions. This securely binds your file/shell tools (the Connector) to that issue's specific environment. DO NOT guess paths.\n" +
        "2. **Execute**: Use the connector tools (`read_file`, `write_file`, `list_dir`, `mkdir`, `git`, `dotnet`, `python`, `http_get`) to perform your work. All relative paths are automatically rooted in the checked-out issue's directory.\n" +
        "3. **Complete**: When the task is done, use 'complete_task' to signal completion. You MUST supply 'resultNotes' detailing your executed work, outputs, and any context needed by the next Role. If you decide the next step, you must supply the explicit 'nextStep' object containing 'id', 'name', and 'role'.\n\n" +
        "### RULES:\n" +
        "- You cannot use file/system tools until you have checked out a issue.\n" +
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
                Description = "Checks out the next task from a issue by ID (Issue ID), binding your environment connector to it so you can use file/system tools.",
                Parameters = new { type = "object", properties = new { issueId = new { type = "string" } }, required = new[] { "issueId" } }
            }
        });

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
                Description = "Stores temporary notes, remarks, or intermediate findings during your task. These are securely saved to the issue comments.",
                Parameters = new { type = "object", properties = new { note = new { type = "string" } }, required = new[] { "note" } }
            }
        });

        definitions.Add(new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = "read_notes",
                Description = "Reads all comments attached to the issue to find context and remarks left by previous agents.",
                Parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            }
        });

        // 3. Connector tool signatures (always exposed, but restricted functionally if not checked out)
        var dummyEnv = new ConnectorEnvironment { Dir = "C:\\" };
        var dummyWorkspace = new LocalWorkspaceConnector(dummyEnv);
        var dummyIssueConfig = new IssueTrackerConfig { Owner = "dummy", Repository = "dummy" };
        var dummyIssue = new GitHubIssueTrackerConnector(dummyIssueConfig, null);
        
        var connectorTools = new List<IAboTool>
        {
            new ReadFileTool(dummyWorkspace),
            new WriteFileTool(dummyWorkspace),
            new DeleteFileTool(dummyWorkspace),
            new ListDirTool(dummyWorkspace),
            new MkDirTool(dummyWorkspace),
            new GitTool(dummyWorkspace),
            new DotnetTool(dummyWorkspace),
            new PythonTool(dummyWorkspace),
            new SearchRegexTool(dummyWorkspace),
            new HttpGetTool(dummyWorkspace),
            new ListIssuesTool(dummyIssue),
            new GetIssueTool(dummyIssue),
            new CreateIssueTool(dummyIssue),
            new AddIssueCommentTool(dummyIssue)
        };

        if (_wikiClient != null)
        {
            var dummyWiki = new XpectoLiveWikiConnector(_wikiClient, "dummy");
            connectorTools.Add(new GetWikiPageTool(dummyWiki));
            connectorTools.Add(new CreateWikiPageTool(dummyWiki));
            connectorTools.Add(new UpdateWikiPageTool(dummyWiki));
            connectorTools.Add(new SearchWikiTool(dummyWiki));
        }

        foreach (var tool in connectorTools)
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

        if (name == "checkout_task") return await HandleCheckoutTaskAsync(args);
        if (name == "complete_task") return await HandleCompleteTaskAsync(args);
        if (name == "request_ceo_help") return HandleRequestCeoHelp(args);
        if (name == "take_notes") return await HandleTakeNotesAsync(args);
        if (name == "read_notes") return await HandleReadNotesAsync(args);

        var globalTool = _globalTools.FirstOrDefault(t => t.Name == name);
        if (globalTool != null) return await globalTool.ExecuteAsync(args);

        var connectorToolNames = new[] { "read_file", "write_file", "delete_file", "list_dir", "mkdir", "git", "dotnet", "python", "search_regex", "http_get", "list_issues", "get_issue", "create_issue", "add_issue_comment", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki" };
        if (connectorToolNames.Contains(name))
        {
            // Specifically exclude checking if workspace is null if it's an issue/wiki tool that doesn't need it?
            if (_currentWorkspace == null || string.IsNullOrEmpty(_currentIssueId))
            {
                return "Error: You must execute 'checkout_task' before using any file, system, or issue tracker tools.";
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

    private async Task<string> HandleCheckoutTaskAsync(string argsJson)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            if (args == null || !args.TryGetValue("issueId", out var issueId)) return "Error: issueId required.";

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

            var currentStepId = _currentIssue.Labels.FirstOrDefault(l => l.StartsWith("step: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim();

            if (nextStepInfo == null && !string.IsNullOrWhiteSpace(currentStepId))
            {
                var transitions = Abo.Core.WorkflowEngine.GetTransitions(currentStepId);

                if (transitions.Count > 1) 
                {
                    var options = string.Join(", ", transitions.Select(t => $"'{t.NextStepId}' (Condition: {t.ConditionName})"));
                    return $"Error: The current step leads to a decision gateway with multiple paths. You MUST provide the 'nextStep' object explicitly (including id, name, and role) so the engine knows which route to take. Valid nextStep.id options based on your decision are: {options}";
                }

                if (transitions.Count == 1)
                {
                    var resolvedId = transitions.First().NextStepId;
                    var stepInfo = Abo.Core.WorkflowEngine.GetStepInfo(resolvedId);
                    if (stepInfo != null)
                    {
                        nextStepInfo = stepInfo;
                        transitions.First().ApplyLabels?.Invoke(_currentIssue.Labels);
                    }
                }
            }
            else if (nextStepInfo != null && !string.IsNullOrWhiteSpace(currentStepId))
            {
                // The agent explicitly specified the next step. Find if it matches a valid transition to apply labels.
                var transitions = Abo.Core.WorkflowEngine.GetTransitions(currentStepId);
                var matchedTransition = transitions.FirstOrDefault(t => t.NextStepId.Equals(nextStepInfo.StepId, StringComparison.OrdinalIgnoreCase));
                matchedTransition?.ApplyLabels?.Invoke(_currentIssue.Labels);
            }

            if (nextStepInfo == null) return "Error: Could not automatically determine the next step. You must supply 'nextStep' with id, name, and role based on the appropriate workflow transition.";

            bool reachedEndEvent = string.Equals(nextStepInfo.StepId, "invalid", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(nextStepInfo.StepId, "done", StringComparison.OrdinalIgnoreCase) ||
                                   string.IsNullOrWhiteSpace(nextStepInfo.RequiredRole);

            if (!string.IsNullOrWhiteSpace(resultNotes))
            {
                await _currentIssueTracker.AddIssueCommentAsync(_currentIssueId, resultNotes);
            }

            var updatedLabels = _currentIssue.Labels.Where(l => !l.StartsWith("step: ") && !l.StartsWith("role: ")).ToList();
            if (!reachedEndEvent)
            {
                updatedLabels.Add($"step: {nextStepInfo.StepId}");
                updatedLabels.Add($"role: {nextStepInfo.RequiredRole}");
                await _currentIssueTracker.UpdateIssueAsync(_currentIssueId, state: "open", labels: updatedLabels.ToArray());
            }
            else
            {
                await _currentIssueTracker.UpdateIssueAsync(_currentIssueId, state: "closed", labels: updatedLabels.ToArray());
            }

            var oldProj = _currentIssueId;
            _currentIssueId = null;
            _currentWorkspace = null;
            _currentIssueTracker = null;
            _currentIssue = null;
            _connectorTools.Clear();

            if (reachedEndEvent) return $"Success. Task completed for issue '{oldProj}'. The issue has reached an end state and is now fully completed.";

            return $"Success. Task completed for issue '{oldProj}'. Advanced to next step: '{nextStepInfo?.StepId}'.";
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

    private async Task<string> HandleTakeNotesAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentIssueId) || _currentIssueTracker == null) return "Error: You must check out a task before taking notes.";

        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson);
            if (args == null || !args.TryGetValue("note", out var note)) return "Error: 'note' is required.";

            await _currentIssueTracker.AddIssueCommentAsync(_currentIssueId, $"### Intermediate Note\n{note}");
            return "Note successfully saved to issue comments.";
        }
        catch (Exception ex) { return $"Error saving note: {ex.Message}"; }
    }

    private async Task<string> HandleReadNotesAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentIssueId) || _currentIssueTracker == null) return "Error: You must check out a task before reading notes.";
        try
        {
            var issue = await _currentIssueTracker.GetIssueAsync(_currentIssueId);
            return issue?.Body ?? "No notes/comments found.";
        }
        catch (Exception ex) { return $"Error reading notes: {ex.Message}"; }
    }


}
