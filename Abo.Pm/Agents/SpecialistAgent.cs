using System.Text.Json;
using Abo.Core.Services;
using Abo.Tools;
using Abo.Contracts.Models;
using Abo.Contracts.OpenAI;
using Abo.Core;
using Abo.Core.Connectors;
using Abo.Core.Models;
using Abo.Tools.Connector;
using Abo.Integrations.GitHub;
using Abo.Integrations.XpectoLive;
using Abo.Integrations.Mattermost;
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
    private readonly MattermostClient? _mattermostClient;
    private readonly MattermostOptions? _mattermostOptions;

    // Persistent consult tool (available regardless of workspace)
    private readonly ConsultSpecialistTool? _consultTool;

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

    public SpecialistAgent(
        IEnumerable<IAboTool> globalTools,
        string roleTitle,
        IConsultationService consultationService,
        string systemPrompt,
        List<string> allowedTools,
        IConfiguration configuration,
        string issueId,
        IXpectoLiveWikiClient? wikiClient = null,
        MattermostClient? mattermostClient = null,
        MattermostOptions? mattermostOptions = null)
    {
        _globalTools = globalTools;
        _roleTitle = roleTitle;
        _rolePrompt = systemPrompt;
        _allowedTools = allowedTools;
        _config = configuration;
        _currentIssueId = issueId;
        _consultTool = new ConsultSpecialistTool(consultationService);
        _issueTrackerToken = configuration["Integrations:GitHub:Token"];
        _wikiClient = wikiClient!;
        _mattermostClient = mattermostClient;
        _mattermostOptions = mattermostOptions;
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
        "3. **Complete**: When the task is done, use 'conclude_step' to signal completion. You MUST supply 'notes' detailing your executed work, outputs, and any context needed by the next Role. (These notes will be automatically added as a comment to the issue, so DO NOT use 'add_issue_comment' to duplicate this information). You MUST supply a 'keyword'. If there are defined transition conditions, use the one matching your decision. Alternatively, you can always use the 'postpone' keyword to suspend the task safely, or 'request_ceo_help' if you are completely blocked.\n\n" +
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
                Name = "conclude_step",
                Description = "Marks the current task in your checked-out issue as completed and updates its status. This will also terminate your session.",
                Parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        keyword = new
                        {
                            type = "string",
                            description = "Required. The routing outcome. Use the condition name of the path to take, or 'postpone' to safely suspend the task and preserve context, or 'request_ceo_help' to abort and request human assistance."
                        },
                        notes = new { type = "string", description = "A detailed summary of the parameters, context, or results generated during this step to store in notes.md for the next person/agent. DO NOT REPEAT STUFF ALREADY STATED IN THE ISSUE OR PREVIOUS NOTES. (Required)" }
                    },
                    required = new[] { "keyword", "notes" }
                }
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

        // Add persistent consult tool (available regardless of workspace)
        if (_consultTool != null)
        {
            definitions.Add(CreateDef(_consultTool));
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

        if (name == "conclude_step") return await HandleConcludeStepAsync(args);

        var globalTool = _globalTools.FirstOrDefault(t => t.Name == name);
        if (globalTool != null) return await globalTool.ExecuteAsync(args);

        var connectorToolNames = new[] { "consult_specialist", "read_file", "write_file", "patch_file", "delete_file", "list_dir", "mkdir", "git", "dotnet", "python", "shell", "search_regex", "http_get", "list_issues", "get_issue", "create_issue", "create_sub_issue", "add_issue_comment", "update_issue", "get_wiki_page", "create_wiki_page", "update_wiki_page", "move_wiki_page", "search_wiki", "list_wiki" };
        if (connectorToolNames.Contains(name))
        {
            // Handle the consult_specialist tool (persistent, not in _connectorTools)
            if (name == "consult_specialist" && _consultTool != null)
            {
                if (_allowedTools != null && _allowedTools.Any() && !string.IsNullOrEmpty(name) && !_allowedTools.Contains(name))
                {
                    return $"Error: Tool '{name}' is restricted and cannot be run by your current role.";
                }
                return await _consultTool.ExecuteAsync(args);
            }

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

            var environmentsFile = Path.Combine(_dataDir, "environments.json");
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
            _connectorTools.Add(new PatchFileTool(_currentWorkspace));
            _connectorTools.Add(new DeleteFileTool(_currentWorkspace));
            _connectorTools.Add(new ListDirTool(_currentWorkspace));
            _connectorTools.Add(new MkDirTool(_currentWorkspace));
            _connectorTools.Add(new GitTool(_currentWorkspace));

            // Technology-based runtime tool filtering
            var tech = targetEnv.Technology?.ToLowerInvariant() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(tech))
            {
                // Fallback: mount all runtime tools; startup validation already logged the error
                _connectorTools.Add(new DotnetTool(_currentWorkspace));
                _connectorTools.Add(new PythonTool(_currentWorkspace));
                _connectorTools.Add(new ShellTool(_currentWorkspace));
            }
            else
            {
                bool includeDotnet = tech == "dotnet" || tech == "mixed";
                bool includePython = tech == "python" || tech == "mixed";
                bool includeShell = tech == "python" || tech == "node" || tech == "mixed";

                if (includeDotnet) _connectorTools.Add(new DotnetTool(_currentWorkspace));
                if (includePython) _connectorTools.Add(new PythonTool(_currentWorkspace));
                if (includeShell) _connectorTools.Add(new ShellTool(_currentWorkspace));
            }

            _connectorTools.Add(new SearchRegexTool(_currentWorkspace));
            _connectorTools.Add(new HttpGetTool(_currentWorkspace));

            _connectorTools.Add(new ListIssuesTool(_currentIssueTracker));
            _connectorTools.Add(new GetIssueTool(_currentIssueTracker));
            _connectorTools.Add(new CreateIssueTool(_currentIssueTracker));
            _connectorTools.Add(new CreateSubIssueTool(_currentIssueTracker));
            _connectorTools.Add(new AddIssueCommentTool(_currentIssueTracker));
            _connectorTools.Add(new UpdateIssueTool(_currentIssueTracker));

            if (_currentWiki != null)
            {
                _connectorTools.Add(new GetWikiPageTool(_currentWiki));
                _connectorTools.Add(new CreateWikiPageTool(_currentWiki));
                _connectorTools.Add(new UpdateWikiPageTool(_currentWiki));
                _connectorTools.Add(new MoveWikiPageTool(_currentWiki));
                _connectorTools.Add(new SearchWikiTool(_currentWiki));
                _connectorTools.Add(new ListWikiTool(_currentWiki));
            }

            return $"Successfully checked out issue/issue '{issueId}'. You are now bound to environment '{targetEnv.Name}' located at '{targetEnv.Dir}'. Your relative paths will root here.";
        }
        catch (Exception ex)
        {
            return $"Checkout error: {ex.Message}";
        }
    }

    private async Task<string> HandleConcludeStepAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentIssueId) || _currentIssueTracker == null || _currentIssue == null) return "Error: No checked-out issue to complete.";

        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            if (args == null || !args.TryGetValue("notes", out var notesElement)) return "Error: 'notes' is required.";
            var notes = notesElement.GetString();

            if (!args.TryGetValue("keyword", out var keywordElement) || keywordElement.ValueKind != JsonValueKind.String)
                return "Error: 'keyword' is required.";
            var keyword = keywordElement.GetString();
            if (string.IsNullOrWhiteSpace(keyword))
                return "Error: 'keyword' must not be empty.";

            if (keyword.Equals("postpone", StringComparison.OrdinalIgnoreCase))
            {
                return await HandlePostponeTaskAsync($"{{\"contextNotes\": {JsonSerializer.Serialize(notes)}}}");
            }
            if (keyword.Equals("request_ceo_help", StringComparison.OrdinalIgnoreCase))
            {
                return HandleRequestCeoHelp($"{{\"message\": {JsonSerializer.Serialize(notes)}}}");
            }

            var currentStatus = Abo.Core.WorkflowEngine.ResolveStatusFallback(_currentIssue);

            WorkflowTransition? matchedTransition = null;

            if (!string.IsNullOrWhiteSpace(currentStatus))
            {
                var transitions = Abo.Core.WorkflowEngine.GetTransitions(_currentIssue);

                if (transitions.Count > 0)
                {
                    if (transitions.TryGetValue(keyword, out var transition))
                    {
                        matchedTransition = transition;
                    }

                    if (matchedTransition == null)
                    {
                        var options = string.Join(", ", transitions.Keys.Select(k => $"'{k}'"));
                        return $"Error: The provided keyword '{keyword}' did not match any routing conditions. Valid expected condition matches are: {options}";
                    }

                    matchedTransition.ApplyState?.Invoke(_currentIssue);
                }
            }

            bool reachedEndEvent = matchedTransition?.IsEndEvent ?? false;

            if (!string.IsNullOrWhiteSpace(notes))
            {
                await _currentIssueTracker.AddIssueCommentAsync(_currentIssueId, notes);
            }

            var updatedLabels = _currentIssue.Labels.Where(l => !l.StartsWith("role: ") && !l.StartsWith("env: ")).ToList();
            await _currentIssueTracker.UpdateIssueAsync(_currentIssueId, state: _currentIssue.State, labels: updatedLabels.ToArray(), project: _currentIssue.Project, status: _currentIssue.Status);

            if (reachedEndEvent)
            {
                // Post a short status comment with aggregated LLM consumption stats (best-effort, non-blocking)
                var consumptionFilePath = Path.Combine(
                    AppContext.BaseDirectory, "Data", "IssueConsumption", $"{_currentIssueId}.json");

                int totalCalls = 0;
                double totalCost = 0.0;
                bool hasData = false;

                if (File.Exists(consumptionFilePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(consumptionFilePath);
                        var record = JsonSerializer.Deserialize<Abo.Core.Models.IssueConsumptionRecord>(json);
                        if (record != null)
                        {
                            totalCalls = record.TotalCalls;
                            totalCost = record.TotalCost;
                            hasData = true;
                        }
                    }
                    catch { /* graceful degradation – ignore read errors */ }
                }

                if (hasData)
                {
                    var statusComment = $"🤖 Completed in {totalCalls} LLM calls | Est. cost: ${totalCost:F4}";
                    try
                    {
                        await _currentIssueTracker.AddIssueCommentAsync(_currentIssueId, statusComment);
                    }
                    catch { /* non-blocking – issue is already closed */ }

                    // Clean up the accumulator file
                    try { File.Delete(consumptionFilePath); } catch { }
                }
            }

            // Capture issueId before clearing state (needed for sentinel return value context)
            var completedIssueId = _currentIssueId;

            _currentIssueId = null;
            _currentWorkspace = null;
            _currentIssueTracker = null;
            _currentIssue = null;
            _connectorTools.Clear();

            // Return the sentinel prefix followed by the notes so the Orchestrator can
            // immediately surface the notes to the user without an extra LLM round-trip.
            // The Orchestrator detects AgentSentinels.ConcludeStepResult and short-circuits the loop.
            return $"{AgentSentinels.ConcludeStepResult}{notes}";
        }
        catch (Exception ex)
        {
            return $"Error completing task: {ex.Message}";
        }
    }

    private async Task TryNotifyReleaseCompletionAsync()
    {
        if (_mattermostClient == null || _mattermostOptions == null ||
            string.IsNullOrWhiteSpace(_mattermostOptions.CeoUserName)) return;

        if (_currentIssueTracker == null) return;

        try
        {
            var allIssues = await _currentIssueTracker.ListIssuesAsync(state: "open");
            var releaseCurrentIssues = allIssues
                .Where(i => string.Equals(i.Project, "release-current", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // If there are no open release-current issues (all done or none exist),
            // also check for any that are closed/done
            if (!releaseCurrentIssues.Any())
            {
                // Get all issues (including closed) to verify there was at least one release-current issue
                var allIssuesIncludingClosed = await _currentIssueTracker.ListIssuesAsync();
                var anyReleaseCurrent = allIssuesIncludingClosed
                    .Any(i => string.Equals(i.Project, "release-current", StringComparison.OrdinalIgnoreCase));

                if (anyReleaseCurrent)
                {
                    var message = "🚀 **Release Ready!**\n\nAll `release-current` issues are now in `done` state. The current release is ready to ship!";
                    await _mattermostClient.SendDirectMessageAsync(_mattermostOptions.CeoUserName, message);
                }
            }
        }
        catch (Exception ex)
        {
            // Non-blocking: log but don't fail the completion
            _ = ex;
        }
    }

    private async Task<string> HandlePostponeTaskAsync(string argsJson)
    {
        if (string.IsNullOrEmpty(_currentIssueId) || _currentIssueTracker == null || _currentIssue == null)
            return "Error: No checked-out issue to postpone.";

        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argsJson);
            if (args == null || !args.TryGetValue("contextNotes", out var contextNotesElement))
                return "Error: 'contextNotes' is required for postpone_task.";

            var contextNotes = contextNotesElement.GetString();
            if (string.IsNullOrWhiteSpace(contextNotes))
                return "Error: 'contextNotes' must not be empty.";

            // Post context comment on the issue — intentionally the ONLY tracker operation
            // (issue state/step/labels are left completely unchanged)
            var commentPrefix = "## ⏸️ Task Postponed — Context for Next Agent Session\n\n";
            await _currentIssueTracker.AddIssueCommentAsync(_currentIssueId, commentPrefix + contextNotes);

            // Clear agent state
            _currentIssueId = null;
            _currentWorkspace = null;
            _currentIssueTracker = null;
            _currentIssue = null;
            _connectorTools.Clear();

            // Return sentinel so Orchestrator can terminate the loop immediately
            return $"{AgentSentinels.PostponeTaskResult}{contextNotes}";
        }
        catch (Exception ex)
        {
            return $"Error postponing task: {ex.Message}";
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
