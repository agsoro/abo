using Abo.Agents;
using Abo.Core;
using Abo.Integrations.Mattermost;
using Abo.Integrations.XpectoLive;
using Abo.Tools;
using Abo.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Register Core Components
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddSingleton<Abo.Core.Services.TrafficLoggerService>();
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


builder.Services.AddTransient<IAboTool, StartIssueTool>();
builder.Services.AddTransient<IAboTool, ListActiveIssuesTool>();
builder.Services.AddTransient<IAboTool, GetEnvironmentsTool>();
builder.Services.AddTransient<IAboTool, GetOpenWorkTool>();

// Register Agents
builder.Services.AddTransient<ManagerAgent>();
builder.Services.AddTransient<IAgent, ManagerAgent>(sp => sp.GetRequiredService<ManagerAgent>());

// Register Background Services
builder.Services.AddSingleton<StartupStatusService>();
builder.Services.AddSingleton<Abo.Core.OpenRouterModelSelector>();
builder.Services.AddHostedService<EnvironmentValidationService>();
builder.Services.AddHostedService<CronjobAutoStartService>();

var app = builder.Build();

app.UseDefaultFiles(); // Serves `/index.html` automatically for `/`
app.UseStaticFiles();  // Serves wwwroot static files

async Task<List<Abo.Core.Connectors.ConnectorEnvironment>> GetConfiguredEnvironmentsAsync()
{
    var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "environments.json");
    if (!File.Exists(environmentsFile)) return new List<Abo.Core.Connectors.ConnectorEnvironment>();

    var envJson = await File.ReadAllTextAsync(environmentsFile);
    return JsonSerializer.Deserialize<List<Abo.Core.Connectors.ConnectorEnvironment>>(envJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
}

// API: Health / Status
app.MapGet("/api/status", (IConfiguration config) =>
{
    var endpoint = config["Config:ApiEndpoint"];
    var model = config["Config:ModelName"];
    var hasKey = !string.IsNullOrEmpty(config["Config:ApiKey"]);
    return Results.Ok(new { Status = "Running", Model = model, HasApiKey = hasKey });
});

app.MapGet("/api/environments", async () =>
{
    var envs = await GetConfiguredEnvironmentsAsync();
    var mapped = envs
        .Where(e => e.IssueTracker != null)
        .Select(e => new { name = e.Name, displayName = e.Name, hasIssueTracker = true })
        .ToList();
    return Results.Ok(mapped);
});



// Helper to get all issues across environments
async Task<List<Abo.Contracts.Models.IssueRecord>> GetAllIssuesAsync(IConfiguration config, IMemoryCache cache)
{
    var cacheKey = "AllActiveIssues";
    if (cache.TryGetValue(cacheKey, out object? cachedObject) && cachedObject is List<Abo.Contracts.Models.IssueRecord> cachedIssues)
    {
        return cachedIssues;
    }

    var envs = await GetConfiguredEnvironmentsAsync();
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
                foreach (var issue in issues)
                {
                    if (!activeIssues.Any(i => i.Id == issue.Id && i.Title == issue.Title)) activeIssues.Add(issue);
                }
            }
            catch { /* Ignore tracker errors */ }
        }
    }

    cache.Set(cacheKey, activeIssues, TimeSpan.FromSeconds(60));
    return activeIssues;
}

// Helper to get a single issue tracker by environment name
async Task<Abo.Core.Connectors.IIssueTrackerConnector?> GetTrackerForEnvironmentAsync(string? environmentName, IConfiguration config)
{
    var envs = await GetConfiguredEnvironmentsAsync();

    // If no environment specified, use the first one with an issue tracker
    var env = string.IsNullOrWhiteSpace(environmentName)
        ? envs.FirstOrDefault(e => e.IssueTracker != null)
        : envs.FirstOrDefault(e => e.Name.Equals(environmentName, StringComparison.OrdinalIgnoreCase) && e.IssueTracker != null);

    if (env?.IssueTracker == null)
        return null;

    if (env.IssueTracker.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
    {
        return new Abo.Integrations.GitHub.GitHubIssueTrackerConnector(env.IssueTracker, config["Integrations:GitHub:Token"], env.Name);
    }
    else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
    {
        return new Abo.Core.Connectors.FileSystemIssueTrackerConnector(env.Name);
    }

    return null;
}

// API: Issues – list all active issues
app.MapGet("/api/issues", async (IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    var issues = await GetAllIssuesAsync(config, cache);
    var mapped = issues.Select(issue => new
    {
        Id = issue.Id,
        Title = issue.Title,
        Type = issue.Labels.FirstOrDefault(l => l.StartsWith("type: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? "",
        Project = issue.Project,   // Issue #205: expose project field for dashboard grouping
        StepId = Abo.Core.WorkflowEngine.StepId.ToStepId(issue),
        CurrentStep = new
        {
            Status = Abo.Core.WorkflowEngine.ResolveStatusFallback(issue),
            StepName = Abo.Core.WorkflowEngine.GetStepInfo(issue)?.StepName ?? "",
            RequiredRole = Abo.Core.WorkflowEngine.GetStepInfo(issue)?.Role?.RoleId ?? ""
        },
        EnvironmentName = issue.Labels.FirstOrDefault(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase))?.Substring(5).Trim() ?? "",
        Status = issue.State,
        ParentId = issue.Labels.FirstOrDefault(l => l.StartsWith("parent: ", StringComparison.OrdinalIgnoreCase))?.Substring(8).Trim()
    }).ToList();

    return Results.Ok(mapped);
});

// API: Issues – fetch the status of a specific issue by ID
app.MapGet("/api/issues/{id}/status", async (string id, IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    var issues = await GetAllIssuesAsync(config, cache);
    var issue = issues.FirstOrDefault(i => i.Id == id);
    if (issue == null) return Results.NotFound($"No status found for issue '{id}'.");

    return Results.Ok(new
    {
        Status = issue.State,
        LastUpdated = DateTime.UtcNow.ToString("O") // We don't have exact last updated on issue easily
    });
});

// API: Issues – create a new issue (Issue #287)
app.MapPost("/api/issues/create", async ([FromBody] CreateIssueRequest req, IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new { error = "Title is required" });
    if (string.IsNullOrWhiteSpace(req.Description)) return Results.BadRequest(new { error = "Description is required" });
    if (string.IsNullOrWhiteSpace(req.Type)) return Results.BadRequest(new { error = "Type is required" });
    if (string.IsNullOrWhiteSpace(req.Size)) return Results.BadRequest(new { error = "Size is required" });

    var title = req.Title.Trim().Substring(0, Math.Min(req.Title.Length, 200));
    var description = req.Description.Trim().Substring(0, Math.Min(req.Description.Length, 2000));
    var type = req.Type.Trim().ToLower();
    var size = req.Size.Trim().ToUpper();
    var validSizes = new[] { "S", "M", "L", "XL" };

    if (!Abo.Contracts.Models.IssueType.IsValid(type))
        return Results.BadRequest(new { error = $"Invalid type. Must be one of: {string.Join(", ", Abo.Contracts.Models.IssueType.AllowedValues)}" });
    if (!validSizes.Contains(size))
        return Results.BadRequest(new { error = $"Invalid size. Must be one of: {string.Join(", ", validSizes)}" });

    try
    {
        var tracker = await GetTrackerForEnvironmentAsync(req.EnvironmentName, config);
        if (tracker == null) return Results.InternalServerError();

        var additionalLabels = new List<string>();
        if (!string.IsNullOrWhiteSpace(req.EnvironmentName)) additionalLabels.Add($"env: {req.EnvironmentName.Trim()}");
        if (!string.IsNullOrWhiteSpace(req.Project)) additionalLabels.Add($"project: {req.Project.Trim()}");

        var newIssue = await tracker.CreateIssueAsync(
            title: title,
            body: description,
            type: type,
            size: size,
            additionalLabels: additionalLabels.Any() ? additionalLabels.ToArray() : null,
            project: req.Project,
            status: "open"
        );

        cache.Remove("AllActiveIssues");

        return Results.Ok(new
        {
            Id = newIssue.Id,
            Title = newIssue.Title,
            Status = "created"
        });
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error creating issue via /api/issues/create");
        return Results.InternalServerError();
    }
});

// API: Active Sessions – list currently active agent sessions
app.MapGet("/api/sessions", (SessionService sessionService) =>
{
    var sessions = sessionService.GetActiveSessions();
    return Results.Ok(sessions);
});

// API: Open Work – list all issues with their current step and open tasks
app.MapGet("/api/open-work", async (IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    var issues = await GetAllIssuesAsync(config, cache);
    var mapped = issues.Where(i => i.State != "closed").Select(issue => new
    {
        IssueId = issue.Id,
        Title = issue.Title,
        Type = issue.Labels.FirstOrDefault(l => l.StartsWith("type: ", StringComparison.OrdinalIgnoreCase))?.Substring(6).Trim() ?? "",
        CurrentStep = new
        {
            Status = Abo.Core.WorkflowEngine.ResolveStatusFallback(issue),
            StepName = Abo.Core.WorkflowEngine.GetStepInfo(issue)?.StepName ?? "",
            RequiredRole = Abo.Core.WorkflowEngine.GetStepInfo(issue)?.Role?.RoleId ?? ""
        },
        EnvironmentName = issue.Labels.FirstOrDefault(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase))?.Substring(5).Trim() ?? "",
        Status = issue.State,
        LastUpdated = DateTime.UtcNow.ToString("O")
    }).ToList();

    return Results.Ok(mapped);
});

// API: LLM Traffic – fetch LLM call/response log entries
app.MapGet("/api/llm-traffic", async (int? limit, Abo.Core.Services.TrafficLoggerService trafficLoggerService) =>
{
    var effectiveLimit = limit.HasValue && limit.Value > 0 ? limit.Value : 100;
    var entries = await trafficLoggerService.GetTrafficAsync(effectiveLimit);
    return Results.Ok(entries);
});

// API: LLM Consumption – fetch aggregated token/cost statistics per agent run
app.MapGet("/api/llm-consumption", async (int? limit, Abo.Core.Services.TrafficLoggerService trafficLoggerService) =>
{
    var effectiveLimit = limit.HasValue && limit.Value > 0 ? limit.Value : 100;
    var entries = await trafficLoggerService.GetConsumptionAsync(effectiveLimit);
    return Results.Ok(entries);
});

// API: Interact – main chat endpoint
app.MapPost("/api/interact", async ([FromBody] InteractRequest req, Orchestrator orchestrator, AgentSupervisor supervisor, UserService userService, MattermostClient mattermostClient, SessionService sessionService) =>
{
    if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest("Message is empty.");

    var sessionId = req.SessionId ?? "web-session";
    var userName = req.UserName ?? "Web User";
    var userId = req.UserId ?? sessionId;

    userService.GetOrCreateUser(userId, userName);

    // Set current issue context if provided
    if (!string.IsNullOrWhiteSpace(req.IssueId))
    {
        sessionService.SetCurrentIssue(sessionId, req.IssueId, req.IssueTitle);
    }

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

app.Lifetime.ApplicationStarted.Register(() =>
{
    Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var mattermostClient = scope.ServiceProvider.GetRequiredService<MattermostClient>();
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MattermostOptions>>().Value;

            if (!string.IsNullOrEmpty(options.CeoUserName))
            {
                var startupStatus = scope.ServiceProvider.GetRequiredService<StartupStatusService>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Hello CEO! ABO has successfully started.");

                if (startupStatus.Errors.Any())
                {
                    sb.AppendLine("\n**⚠ Startup Configuration Errors:**");
                    foreach (var err in startupStatus.Errors)
                    {
                        sb.AppendLine($"- {err}");
                    }
                }

                sb.AppendLine("\n**📊 Open Work & Projects:**");
                var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                var issues = await GetAllIssuesAsync(config, cache);
                if (issues.Any())
                {
                    var byProject = issues.GroupBy(i => string.IsNullOrWhiteSpace(i.Project) ? "Unassigned" : i.Project);
                    foreach (var group in byProject)
                    {
                        sb.AppendLine($"\n*{group.Key}*:");
                        foreach (var issue in group)
                        {
                            var step = Abo.Core.WorkflowEngine.ResolveStatusFallback(issue);
                            var role = Abo.Core.WorkflowEngine.GetStepInfo(issue)?.Role?.RoleId ?? "Any";
                            var envName = issue.Labels.FirstOrDefault(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase))?.Substring(5).Trim() ?? "?";
                            sb.AppendLine($"- [{issue.Id}] {issue.Title} (Env: {envName}, Step: {step}, Role: {role})");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("- No open issues found.");
                }

                await mattermostClient.SendDirectMessageAsync(options.CeoUserName, sb.ToString());
            }
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Failed to send startup greeting to CEO.");
        }
    });
});

// Initialize OpenRouter models dynamically before starting the server
var appSettingsPath = Path.Combine(app.Environment.ContentRootPath, "appsettings.json");
var modelSelector = app.Services.GetRequiredService<Abo.Core.OpenRouterModelSelector>();
await modelSelector.UpdateModelsIfRequiredAsync(appSettingsPath);

// Force reload configuration immediately to ensure AgentSupervisor sees the new models
if (app.Services.GetRequiredService<IConfiguration>() is IConfigurationRoot configRoot)
{
    configRoot.Reload();
}

app.Run();

public class CreateIssueRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string? Project { get; set; }
    public string? EnvironmentName { get; set; }
}

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

    /// <summary>
    /// Optional: The ID of the issue being processed by this agent session.
    /// When provided, this context is tracked in SessionService and returned
    /// by GET /api/sessions for real-time dashboard feedback.
    /// </summary>
    public string? IssueId { get; set; }

    /// <summary>
    /// Optional: The title of the issue being processed by this agent session.
    /// Used alongside IssueId for enhanced user feedback on the dashboard.
    /// </summary>
    public string? IssueTitle { get; set; }
}
