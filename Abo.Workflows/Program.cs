using Abo.Agents;
using Abo.Core;
using Abo.Integrations.Mattermost;
using Abo.Integrations.XpectoLive;
using Abo.Tools;
using Abo.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Register Core Components
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddHttpClient<Orchestrator>(client => client.Timeout = TimeSpan.FromSeconds(600));
builder.Services.AddHttpClient<AgentSupervisor>(client => client.Timeout = TimeSpan.FromSeconds(600));

// Register Integrations
builder.Services.Configure<XpectoLiveOptions>(builder.Configuration.GetSection("Integrations:XpectoLive"));
builder.Services.AddHttpClient<XpectoLiveClient>();
builder.Services.AddHttpClient<IXpectoLiveWikiClient, XpectoLiveWikiClient>();

builder.Services.Configure<MattermostOptions>(builder.Configuration.GetSection("Integrations:Mattermost"));
builder.Services.AddHttpClient<MattermostClient>();
builder.Services.AddHostedService<MattermostListenerService>();

// Register Tools
builder.Services.AddTransient<IAboTool, GetSystemTimeTool>();

builder.Services.AddTransient<IAboTool, CreateProcessTool>();
builder.Services.AddTransient<IAboTool, UpdateProcessTool>();
builder.Services.AddTransient<IAboTool, CheckBpmnTool>();
builder.Services.AddTransient<IAboTool, StartProjectTool>();
builder.Services.AddTransient<IAboTool, ListProjectsTool>();
builder.Services.AddTransient<IAboTool, UpsertRoleTool>();
builder.Services.AddTransient<IAboTool, GetRolesTool>();
builder.Services.AddTransient<IAboTool, GetEnvironmentsTool>();
builder.Services.AddTransient<IAboTool, GetOpenWorkTool>();

// Register Agents

builder.Services.AddTransient<PmoAgent>();
builder.Services.AddTransient<IAgent, PmoAgent>(sp => sp.GetRequiredService<PmoAgent>());
builder.Services.AddTransient<ManagerAgent>();
builder.Services.AddTransient<IAgent, ManagerAgent>(sp => sp.GetRequiredService<ManagerAgent>());

// Register Background Services

builder.Services.AddHostedService<EnvironmentValidationService>();

var app = builder.Build();

app.UseDefaultFiles(); // Serves `/index.html` automatically for `/`
app.UseStaticFiles();  // Serves wwwroot static files

// API: Health / Status
app.MapGet("/api/status", (IConfiguration config) =>
{
    var endpoint = config["Config:ApiEndpoint"];
    var model = config["Config:ModelName"];
    var hasKey = !string.IsNullOrEmpty(config["Config:ApiKey"]);
    return Results.Ok(new { Status = "Running", Model = model, HasApiKey = hasKey });
});

// API: Processes – list all process IDs
app.MapGet("/api/processes", () =>
{
    var processesDir = Path.Combine(AppContext.BaseDirectory, "Data", "Processes");
    if (!Directory.Exists(processesDir)) return Results.Ok(new List<string>());

    var files = Directory.GetFiles(processesDir, "*.bpmn")
                         .Select(f => Path.GetFileNameWithoutExtension(f))
                         .ToList();
    return Results.Ok(files);
});

// API: Processes – fetch BPMN XML by ID
app.MapGet("/api/processes/{id}", async (string id) =>
{
    // Basic sanitization
    if (id.Contains("..") || id.Contains("/") || id.Contains("\\")) return Results.BadRequest("Invalid process id.");

    var path = Path.Combine(AppContext.BaseDirectory, "Data", "Processes", $"{id}.bpmn");
    if (!File.Exists(path)) return Results.NotFound();

    var xml = await File.ReadAllTextAsync(path);
    return Results.Text(xml, "application/xml");
});

// Helper to get all issues across environments
async Task<List<Abo.Contracts.Models.IssueRecord>> GetAllIssuesAsync(IConfiguration config)
{
    var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "Environments", "environments.json");
    var envs = new List<Abo.Core.Connectors.ConnectorEnvironment>();
    if (File.Exists(environmentsFile))
    {
        var envJson = await File.ReadAllTextAsync(environmentsFile);
        envs = JsonSerializer.Deserialize<List<Abo.Core.Connectors.ConnectorEnvironment>>(envJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    var activeIssues = new List<Abo.Contracts.Models.IssueRecord>();

    foreach (var env in envs.Where(e => e.IssueTracker != null))
    {
        Abo.Core.Connectors.IIssueTrackerConnector? tracker = null;
        if (env.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
        {
            tracker = new Abo.Integrations.GitHub.GitHubIssueTrackerConnector(env.IssueTracker, config["Integrations:GitHub:Token"], env.Name);
        }
        else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
        {
            tracker = new Abo.Core.Connectors.FileSystemIssueTrackerConnector(env.Name);
        }

        if (tracker != null)
        {
            try
            {
                var issues = await tracker.ListIssuesAsync();
                foreach(var issue in issues) {
                    if (!activeIssues.Any(i => i.Id == issue.Id && i.Title == issue.Title)) activeIssues.Add(issue);
                }
            }
            catch { /* Ignore tracker errors */ }
        }
    }
    return activeIssues;
}

// API: Projects – list all active projects
app.MapGet("/api/projects", async (IConfiguration config) =>
{
    var issues = await GetAllIssuesAsync(config);
    var mapped = issues.Select(issue => new
    {
        Id = issue.Id,
        Title = issue.Title,
        TypeId = issue.Labels.FirstOrDefault(l => l.StartsWith("type: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? "",
        CurrentStep = new {
            StepId = issue.Labels.FirstOrDefault(l => l.StartsWith("step: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? "",
            StepName = "",
            RequiredRole = issue.Labels.FirstOrDefault(l => l.StartsWith("role: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? ""
        },
        EnvironmentName = issue.Labels.FirstOrDefault(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase))?.Substring(5).Trim() ?? "",
        Status = issue.State,
        ParentId = issue.Labels.FirstOrDefault(l => l.StartsWith("parent: ", StringComparison.OrdinalIgnoreCase))?.Substring(8).Trim()
    }).ToList();

    return Results.Ok(mapped);
});

// API: Projects – fetch the status of a specific project by ID
app.MapGet("/api/projects/{id}/status", async (string id, IConfiguration config) =>
{
    var issues = await GetAllIssuesAsync(config);
    var issue = issues.FirstOrDefault(i => i.Id == id);
    if (issue == null) return Results.NotFound($"No status found for project '{id}'.");
    
    return Results.Ok(new {
        Status = issue.State,
        LastUpdated = DateTime.UtcNow.ToString("O") // We don't have exact last updated on issue easily
    });
});

// API: Active Sessions – list currently active agent sessions
app.MapGet("/api/sessions", (SessionService sessionService) =>
{
    var sessions = sessionService.GetActiveSessions();
    return Results.Ok(sessions);
});

// API: Open Work – list all projects with their current step and open tasks
app.MapGet("/api/open-work", async (IConfiguration config) =>
{
    var issues = await GetAllIssuesAsync(config);
    var mapped = issues.Where(i => i.State != "closed").Select(issue => new
    {
        ProjectId = issue.Id,
        Title = issue.Title,
        TypeId = issue.Labels.FirstOrDefault(l => l.StartsWith("type: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? "",
        CurrentStep = new {
            StepId = issue.Labels.FirstOrDefault(l => l.StartsWith("step: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? "",
            StepName = "",
            RequiredRole = issue.Labels.FirstOrDefault(l => l.StartsWith("role: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? ""
        },
        EnvironmentName = issue.Labels.FirstOrDefault(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase))?.Substring(5).Trim() ?? "",
        Status = issue.State,
        LastUpdated = DateTime.UtcNow.ToString("O")
    }).ToList();

    return Results.Ok(mapped);
});

// API: LLM Traffic – fetch LLM call/response log entries
app.MapGet("/api/llm-traffic", async (HttpContext httpContext) =>
{
    var limitParam = httpContext.Request.Query["limit"].FirstOrDefault();
    var limit = int.TryParse(limitParam, out var parsedLimit) && parsedLimit > 0 ? parsedLimit : 100;

    var logPath = Path.Combine(AppContext.BaseDirectory, "Data", "llm_traffic.jsonl");
    if (!File.Exists(logPath)) return Results.Ok(new List<object>());

    var lines = await File.ReadAllLinesAsync(logPath);

    var entries = new List<JsonElement>();
    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        try
        {
            var entry = JsonSerializer.Deserialize<JsonElement>(line);
            entries.Add(entry);
        }
        catch
        {
            // Skip malformed lines
        }
    }

    // Return newest entries first, limited by the limit parameter
    var result = entries.AsEnumerable().Reverse().Take(limit).ToList();
    return Results.Ok(result);
});

// API: LLM Consumption – fetch aggregated token/cost statistics per agent run
app.MapGet("/api/llm-consumption", async (HttpContext httpContext) =>
{
    var limitParam = httpContext.Request.Query["limit"].FirstOrDefault();
    var limit = int.TryParse(limitParam, out var parsedLimit) && parsedLimit > 0 ? parsedLimit : 100;

    var logPath = Path.Combine(AppContext.BaseDirectory, "Data", "llm_consumption.jsonl");
    if (!File.Exists(logPath)) return Results.Ok(new List<object>());

    var lines = await File.ReadAllLinesAsync(logPath);

    var entries = new List<JsonElement>();
    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line)) continue;
        try
        {
            var entry = JsonSerializer.Deserialize<JsonElement>(line);
            entries.Add(entry);
        }
        catch
        {
            // Skip malformed lines
        }
    }

    // Return newest entries first, limited by the limit parameter
    var result = entries.AsEnumerable().Reverse().Take(limit).ToList();
    return Results.Ok(result);
});

// API: Interact – main chat endpoint
app.MapPost("/api/interact", async ([FromBody] InteractRequest req, Orchestrator orchestrator, AgentSupervisor supervisor, UserService userService, MattermostClient mattermostClient) =>
{
    if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest("Message is empty.");

    var sessionId = req.SessionId ?? "web-session";
    var userName = req.UserName ?? "Web User";
    var userId = req.UserId ?? sessionId;

    userService.GetOrCreateUser(userId, userName);

    var history = orchestrator.GetSessionHistory(sessionId);
    var agent = await supervisor.GetBestAgentAsync(req.Message, history);

    string response;

    // If a ChannelId is provided, send a "typing..." indicator to Mattermost
    if (!string.IsNullOrEmpty(req.ChannelId))
    {
        using var typingCts = new CancellationTokenSource();
        var typingTask = Task.Run(async () =>
        {
            while (!typingCts.Token.IsCancellationRequested)
            {
                await mattermostClient.SendTypingAsync(req.ChannelId, req.ParentId);
                try { await Task.Delay(TimeSpan.FromSeconds(5), typingCts.Token); }
                catch (OperationCanceledException) { break; }
            }
        }, typingCts.Token);

        try
        {
            response = await orchestrator.RunAgentLoopAsync(agent, req.Message, sessionId, req.UserName, userId);
        }
        finally
        {
            await typingCts.CancelAsync();
            await typingTask.ConfigureAwait(false);
        }
    }
    else
    {
        response = await orchestrator.RunAgentLoopAsync(agent, req.Message, sessionId, req.UserName, userId);
    }

    return Results.Ok(new { Output = response });
});

app.Run();

public class InteractRequest
{
    public string Message { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }

    /// <summary>
    /// Optional: Mattermost channel ID. When provided, a "typing..." indicator
    /// will be sent to this channel while the agent is processing the request.
    /// </summary>
    public string? ChannelId { get; set; }

    /// <summary>
    /// Optional: Mattermost thread/parent post ID. Used together with ChannelId
    /// to show the typing indicator in the correct thread.
    /// </summary>
    public string? ParentId { get; set; }
}
