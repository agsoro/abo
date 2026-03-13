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
builder.Services.AddTransient<IAboTool, AskMultipleChoiceTool>();
builder.Services.AddTransient<IAboTool, SubscribeQuizTool>();
builder.Services.AddTransient<IAboTool, UnsubscribeQuizTool>();
builder.Services.AddTransient<IAboTool, GetQuizLeaderboardTool>();
builder.Services.AddTransient<IAboTool, UpdateQuizScoreTool>();
builder.Services.AddTransient<IAboTool, AskQuizQuestionTool>();
builder.Services.AddTransient<IAboTool, GetRandomQuestionTool>();
builder.Services.AddTransient<IAboTool, AddQuizQuestionTool>();
builder.Services.AddTransient<IAboTool, GetQuizTopicsTool>();

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
builder.Services.AddTransient<HelloWorldAgent>();
builder.Services.AddTransient<IAgent, HelloWorldAgent>(sp => sp.GetRequiredService<HelloWorldAgent>());
builder.Services.AddTransient<QuizAgent>();
builder.Services.AddTransient<IAgent, QuizAgent>(sp => sp.GetRequiredService<QuizAgent>());
builder.Services.AddTransient<PmoAgent>();
builder.Services.AddTransient<IAgent, PmoAgent>(sp => sp.GetRequiredService<PmoAgent>());
builder.Services.AddTransient<ManagerAgent>();
builder.Services.AddTransient<IAgent, ManagerAgent>(sp => sp.GetRequiredService<ManagerAgent>());

// Register Background Services
builder.Services.AddHostedService<QuizService>();

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

// API: Projects – list all active projects
app.MapGet("/api/projects", async () =>
{
    var projectsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Projects", "active_projects.json");
    if (!File.Exists(projectsPath)) return Results.Ok(new List<object>());

    var json = await File.ReadAllTextAsync(projectsPath);
    var projects = JsonSerializer.Deserialize<JsonElement>(json);
    return Results.Ok(projects);
});

// API: Projects – fetch the status of a specific project by ID
app.MapGet("/api/projects/{id}/status", async (string id) =>
{
    // Basic sanitization
    if (id.Contains("..") || id.Contains("/") || id.Contains("\\")) return Results.BadRequest("Invalid project id.");

    var statusPath = Path.Combine(AppContext.BaseDirectory, "Data", "Projects", id, "status.json");
    if (!File.Exists(statusPath)) return Results.NotFound($"No status file found for project '{id}'.");

    var json = await File.ReadAllTextAsync(statusPath);
    var status = JsonSerializer.Deserialize<JsonElement>(json);
    return Results.Ok(status);
});

// API: Active Sessions – list currently active agent sessions
app.MapGet("/api/sessions", (SessionService sessionService) =>
{
    var sessions = sessionService.GetActiveSessions();
    return Results.Ok(sessions);
});

// API: Open Work – list all projects with their current step and open tasks
app.MapGet("/api/open-work", async () =>
{
    var projectsPath = Path.Combine(AppContext.BaseDirectory, "Data", "Projects", "active_projects.json");
    if (!File.Exists(projectsPath)) return Results.Ok(new List<object>());

    var json = await File.ReadAllTextAsync(projectsPath);
    var projectsElement = JsonSerializer.Deserialize<JsonElement>(json);

    var result = new List<object>();

    foreach (var project in projectsElement.EnumerateArray())
    {
        var projectId = project.GetProperty("Id").GetString() ?? "";
        var title = project.GetProperty("Title").GetString() ?? "";
        var typeId = project.TryGetProperty("TypeId", out var t) ? t.GetString() ?? "" : "";
        var currentStep = project.TryGetProperty("CurrentStep", out var cs) ? cs : default;
        var status = project.TryGetProperty("Status", out var s) ? s.GetString() ?? "" : "";
        var environmentName = project.TryGetProperty("EnvironmentName", out var e) ? e.GetString() ?? "" : "";

        // Read LastUpdated from project status file
        string? lastUpdated = null;
        var statusPath = Path.Combine(AppContext.BaseDirectory, "Data", "Projects", projectId, "status.json");
        if (File.Exists(statusPath))
        {
            try
            {
                var statusJson = await File.ReadAllTextAsync(statusPath);
                var statusEl = JsonSerializer.Deserialize<JsonElement>(statusJson);
                if (statusEl.TryGetProperty("LastUpdated", out var lu))
                    lastUpdated = lu.GetString();
            }
            catch { /* ignore */ }
        }

        result.Add(new
        {
            ProjectId = projectId,
            Title = title,
            TypeId = typeId,
            CurrentStep = currentStep,
            Status = status,
            EnvironmentName = environmentName,
            LastUpdated = lastUpdated
        });
    }

    return Results.Ok(result);
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
