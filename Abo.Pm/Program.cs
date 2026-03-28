using Abo.Agents;
using Abo.Core;
using Abo.Core.Connectors;
using Abo.Core.Models;
using Abo.Integrations.Mattermost;
using Abo.Integrations.XpectoLive;
using Abo.Services;
using Abo.Tools;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;
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

// Register Auth Service
var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
builder.Services.Configure<Abo.Core.Services.AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton<Abo.Core.Services.AuthService>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Abo.Core.Services.AuthOptions>>();

    // Ensure data directory exists
    if (!Directory.Exists(dataDirectory))
        Directory.CreateDirectory(dataDirectory);

    return new Abo.Core.Services.AuthService(dataDirectory, options, sp.GetRequiredService<ILogger<Abo.Core.Services.AuthService>>());
});

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
builder.Services.AddHostedService<AuthCleanupService>();

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

// Helper to save environments to file
async Task SaveEnvironmentsAsync(List<Abo.Core.Connectors.ConnectorEnvironment> envs, string filePath)
{
    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        Directory.CreateDirectory(directory);

    var json = JsonSerializer.Serialize(envs, new JsonSerializerOptions 
    { 
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    await File.WriteAllTextAsync(filePath, json);
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

// ── Authentication API ─────────────────────────────────────────────────────────

// Extract Bearer token from Authorization header
string? ExtractBearerToken(HttpContext context)
{
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;
    return authHeader.Substring(7);
}

// Require authentication middleware helper
async Task<(bool IsAuthenticated, string? Username)> RequireAuthAsync(HttpContext context, Abo.Core.Services.AuthService authService)
{
    var token = ExtractBearerToken(context);
    if (string.IsNullOrEmpty(token))
        return (false, null);

    var (valid, username) = await authService.ValidateSessionAsync(token);
    return (valid, username);
}

// POST /api/auth/login – Authenticate user and return session token
app.MapPost("/api/auth/login", async (
    [FromBody] LoginRequest request,
    Abo.Core.Services.AuthService authService,
    HttpContext context) =>
{
    var ipAddress = context.Connection.RemoteIpAddress?.ToString();
    var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();

    var (success, token, error) = await authService.LoginAsync(
        request.Username,
        request.Password,
        ipAddress,
        userAgent);

    if (!success)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new { Token = token, Username = request.Username });
});

// POST /api/auth/logout – Invalidate session token
app.MapPost("/api/auth/logout", async (
    HttpContext context,
    Abo.Core.Services.AuthService authService) =>
{
    var token = ExtractBearerToken(context);
    if (string.IsNullOrEmpty(token))
        return Results.BadRequest(new { error = "No token provided" });

    await authService.LogoutAsync(token);
    return Results.Ok(new { message = "Logged out successfully" });
});

// GET /api/auth/me – Get current authenticated user info
app.MapGet("/api/auth/me", async (
    HttpContext context,
    Abo.Core.Services.AuthService authService) =>
{
    var (isAuthenticated, username) = await RequireAuthAsync(context, authService);
    if (!isAuthenticated || string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    var user = await authService.GetUserAsync(username);
    if (user == null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new
    {
        Username = user.Username,
        Roles = user.Roles,
        PasswordChanged = user.PasswordChanged,
        CreatedAt = user.CreatedAt,
        LastPasswordChange = user.LastPasswordChange
    });
});

// POST /api/auth/change-password – Change current user's password
app.MapPost("/api/auth/change-password", async (
    HttpContext context,
    [FromBody] ChangePasswordRequest request,
    Abo.Core.Services.AuthService authService) =>
{
    var (isAuthenticated, username) = await RequireAuthAsync(context, authService);
    if (!isAuthenticated || string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    var (success, error) = await authService.ChangePasswordAsync(
        username,
        request.CurrentPassword,
        request.NewPassword);

    if (!success)
    {
        return Results.BadRequest(new { error });
    }

    return Results.Ok(new { message = "Password changed successfully. Please login again." });
});

// ── Admin Auth API ─────────────────────────────────────────────────────────────

// POST /api/admin/auth/init-password – Create user and send initial password via Mattermost (admin only)
app.MapPost("/api/admin/auth/init-password", async (
    HttpContext context,
    [FromBody] InitPasswordRequest request,
    Abo.Core.Services.AuthService authService,
    MattermostClient mattermostClient) =>
{
    // For now, require Bearer token authentication to access admin endpoints
    var (isAuthenticated, username) = await RequireAuthAsync(context, authService);
    if (!isAuthenticated || string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    // Check if user has admin role
    var user = await authService.GetUserAsync(username);
    if (user == null || !user.Roles.Contains("admin"))
    {
        return Results.Forbid();
    }

    var roles = request.Roles?.Count > 0 ? request.Roles : new List<string> { "user" };
    
    var (success, password, error) = await authService.CreateUserAsync(
        request.Username,
        roles,
        mattermostClient,
        request.MattermostUsername);

    if (!success)
    {
        return Results.BadRequest(new { error });
    }

    return Results.Created($"/api/admin/auth/users/{request.Username}", new
    {
        Username = request.Username,
        InitialPassword = password,
        Message = password != null 
            ? "User created. Initial password sent via Mattermost."
            : "User created. Check logs for initial password (Mattermost not configured)."
    });
});

// GET /api/admin/auth/users – List all users (admin only)
app.MapGet("/api/admin/auth/users", async (
    HttpContext context,
    Abo.Core.Services.AuthService authService) =>
{
    var (isAuthenticated, username) = await RequireAuthAsync(context, authService);
    if (!isAuthenticated || string.IsNullOrEmpty(username))
    {
        return Results.Unauthorized();
    }

    var user = await authService.GetUserAsync(username);
    if (user == null || !user.Roles.Contains("admin"))
    {
        return Results.Forbid();
    }

    var users = await authService.ListUsersAsync();
    return Results.Ok(users);
});

// DELETE /api/admin/auth/users/{username} – Delete a user (admin only)
app.MapDelete("/api/admin/auth/users/{username}", async (
    HttpContext context,
    string username,
    Abo.Core.Services.AuthService authService) =>
{
    var (isAuthenticated, currentUser) = await RequireAuthAsync(context, authService);
    if (!isAuthenticated || string.IsNullOrEmpty(currentUser))
    {
        return Results.Unauthorized();
    }

    var user = await authService.GetUserAsync(currentUser);
    if (user == null || !user.Roles.Contains("admin"))
    {
        return Results.Forbid();
    }

    var (success, error) = await authService.DeleteUserAsync(username);
    if (!success)
    {
        return Results.BadRequest(new { error });
    }

    return Results.NoContent();
});

// POST /api/admin/auth/reset-password/{username} – Reset a user's password (admin only)
app.MapPost("/api/admin/auth/reset-password/{username}", async (
    HttpContext context,
    string username,
    [FromBody] ResetPasswordRequest? request,
    Abo.Core.Services.AuthService authService,
    MattermostClient mattermostClient) =>
{
    var (isAuthenticated, currentUser) = await RequireAuthAsync(context, authService);
    if (!isAuthenticated || string.IsNullOrEmpty(currentUser))
    {
        return Results.Unauthorized();
    }

    var user = await authService.GetUserAsync(currentUser);
    if (user == null || !user.Roles.Contains("admin"))
    {
        return Results.Forbid();
    }

    var targetUsername = request?.MattermostUsername;
    var (success, password, error) = await authService.ResetPasswordAsync(
        username,
        mattermostClient,
        targetUsername);

    if (!success)
    {
        return Results.BadRequest(new { error });
    }

    return Results.Ok(new
    {
        Username = username,
        NewPassword = password,
        Message = password != null
            ? "Password reset. New password sent via Mattermost."
            : "Password reset. Check logs for new password (Mattermost not configured)."
    });
});

// ── Admin API: Environment Management ─────────────────────────────────────

// GET /api/admin/environments – Returns ALL environments for management UI
app.MapGet("/api/admin/environments", async () =>
{
    var envs = await GetConfiguredEnvironmentsAsync();
    return Results.Ok(envs);
});

// POST /api/admin/environments – Creates a new environment
app.MapPost("/api/admin/environments", async ([FromBody] Abo.Core.Connectors.ConnectorEnvironment newEnv, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    if (string.IsNullOrWhiteSpace(newEnv.Name))
        return Results.BadRequest(new { error = "Name is required" });

    // Validate name format (alphanumeric with hyphens)
    if (!Regex.IsMatch(newEnv.Name, @"^[a-zA-Z0-9\-]+$"))
        return Results.BadRequest(new { error = "Name must be alphanumeric with hyphens only" });

    var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "environments.json");
    var envs = await GetConfiguredEnvironmentsAsync();

    // Check for duplicate name
    if (envs.Any(e => e.Name.Equals(newEnv.Name, StringComparison.OrdinalIgnoreCase)))
        return Results.BadRequest(new { error = $"Environment '{newEnv.Name}' already exists" });

    // Set defaults
    newEnv.Type ??= "local";
    newEnv.Os ??= "win";
    newEnv.Technology ??= "dotnet";

    // Validate IssueTracker if provided
    if (newEnv.IssueTracker != null)
    {
        if (string.IsNullOrWhiteSpace(newEnv.IssueTracker.Type))
            return Results.BadRequest(new { error = "IssueTracker.Type is required when IssueTracker is configured" });
        if (string.IsNullOrWhiteSpace(newEnv.IssueTracker.Owner))
            return Results.BadRequest(new { error = "IssueTracker.Owner is required when IssueTracker is configured" });
        if (string.IsNullOrWhiteSpace(newEnv.IssueTracker.Repository))
            return Results.BadRequest(new { error = "IssueTracker.Repository is required when IssueTracker is configured" });
    }

    envs.Add(newEnv);
    await SaveEnvironmentsAsync(envs, environmentsFile);
    cache.Remove("AllActiveIssues");

    return Results.Created($"/api/admin/environments/{newEnv.Name}", newEnv);
});

// PUT /api/admin/environments/{name} – Updates an existing environment
app.MapPut("/api/admin/environments/{name}", async (string name, [FromBody] Abo.Core.Connectors.ConnectorEnvironment updatedEnv, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "Environment name is required" });

    // If renaming, validate new name format
    if (!name.Equals(updatedEnv.Name, StringComparison.OrdinalIgnoreCase))
    {
        if (!Regex.IsMatch(updatedEnv.Name, @"^[a-zA-Z0-9\-]+$"))
            return Results.BadRequest(new { error = "Name must be alphanumeric with hyphens only" });
    }

    var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "environments.json");
    var envs = await GetConfiguredEnvironmentsAsync();

    var existing = envs.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (existing == null)
        return Results.NotFound($"Environment '{name}' not found");

    // Check for duplicate if name is being changed
    if (!name.Equals(updatedEnv.Name, StringComparison.OrdinalIgnoreCase) &&
        envs.Any(e => e.Name.Equals(updatedEnv.Name, StringComparison.OrdinalIgnoreCase)))
        return Results.BadRequest(new { error = $"Environment '{updatedEnv.Name}' already exists" });

    // Validate IssueTracker if provided
    if (updatedEnv.IssueTracker != null)
    {
        if (string.IsNullOrWhiteSpace(updatedEnv.IssueTracker.Type))
            return Results.BadRequest(new { error = "IssueTracker.Type is required when IssueTracker is configured" });
        if (string.IsNullOrWhiteSpace(updatedEnv.IssueTracker.Owner))
            return Results.BadRequest(new { error = "IssueTracker.Owner is required when IssueTracker is configured" });
        if (string.IsNullOrWhiteSpace(updatedEnv.IssueTracker.Repository))
            return Results.BadRequest(new { error = "IssueTracker.Repository is required when IssueTracker is configured" });
    }

    // Remove old entry and add updated one
    envs.Remove(existing);
    envs.Add(updatedEnv);
    await SaveEnvironmentsAsync(envs, environmentsFile);
    cache.Remove("AllActiveIssues");

    return Results.Ok(updatedEnv);
});

// DELETE /api/admin/environments/{name} – Deletes an environment
app.MapDelete("/api/admin/environments/{name}", async (string name, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "Environment name is required" });

    var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "environments.json");
    var envs = await GetConfiguredEnvironmentsAsync();

    var existing = envs.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (existing == null)
        return Results.NotFound($"Environment '{name}' not found");

    envs.Remove(existing);
    await SaveEnvironmentsAsync(envs, environmentsFile);
    cache.Remove("AllActiveIssues");

    return Results.NoContent();
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

// Helper to get a wiki connector by environment name
async Task<IWikiConnector?> GetWikiConnectorForEnvironmentAsync(string environmentName, IServiceProvider services)
{
    var envs = await GetConfiguredEnvironmentsAsync();
    var env = envs.FirstOrDefault(e => e.Name.Equals(environmentName, StringComparison.OrdinalIgnoreCase));
    
    if (env == null)
        return null;

    // Check if environment has wiki configured
    if (env.Wiki == null || string.IsNullOrWhiteSpace(env.Wiki.Type))
    {
        // Fall back to filesystem wiki based on environment directory
        return new FileSystemWikiConnector(env);
    }

    // Create appropriate wiki connector based on type
    if (env.Wiki.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
    {
        return new FileSystemWikiConnector(env);
    }
    else if (env.Wiki.Type.Equals("xpectolive", StringComparison.OrdinalIgnoreCase))
    {
        // Get XpectoLive client from services
        var wikiClient = services.GetRequiredService<IXpectoLiveWikiClient>();
        return new XpectoLiveWikiConnector(wikiClient, env.Wiki.RootPath);
    }

    // Default to filesystem
    return new FileSystemWikiConnector(env);
}

// ── Wiki API: ABO Dashboard Wiki Integration ─────────────────────────────────

// GET /api/wiki/{environmentName}/pages – Lists all wiki pages for an environment
app.MapGet("/api/wiki/{environmentName}/pages", async (string environmentName, string? parentPath, IServiceProvider services) =>
{
    try
    {
        var connector = await GetWikiConnectorForEnvironmentAsync(environmentName, services);
        if (connector == null)
        {
            return Results.NotFound($"Environment '{environmentName}' not found");
        }

        var pages = await connector.ListPagesAsync(parentPath);
        var mapped = pages.Select(p => new
        {
            path = p.Path,
            title = p.Title,
            lastModified = p.LastModified,
            parentPath = p.ParentPath
        }).ToList();

        return Results.Ok(mapped);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error listing wiki pages: {ex.Message}");
    }
});

// GET /api/wiki/{environmentName}/page/{*path} – Gets a specific wiki page
app.MapGet("/api/wiki/{environmentName}/page/{**path}", async (string environmentName, string path, IServiceProvider services) =>
{
    try
    {
        var connector = await GetWikiConnectorForEnvironmentAsync(environmentName, services);
        if (connector == null)
        {
            return Results.NotFound($"Environment '{environmentName}' not found");
        }

        var content = await connector.GetPageAsync(path);
        
        if (content.StartsWith("Error:"))
        {
            return Results.NotFound(content);
        }

        // Extract title from content (first H1 heading)
        var title = ExtractTitleFromMarkdown(content) ?? Path.GetFileNameWithoutExtension(path);
        
        // Get page metadata (for filesystem wiki, we can get last modified)
        DateTime? lastModified = null;
        if (connector is FileSystemWikiConnector fsConnector)
        {
            // We don't have direct access to the file info from the connector interface
            // The lastModified will be available in the page summary endpoint
        }

        return Results.Ok(new
        {
            path = path,
            content = content,
            title = title,
            lastModified = lastModified
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error getting wiki page: {ex.Message}");
    }
});

// Helper to extract title from markdown content
static string? ExtractTitleFromMarkdown(string content)
{
    var lines = content.Split('\n');
    foreach (var line in lines)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("# "))
        {
            return trimmed.Substring(2).Trim();
        }
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

// API: Issues – Get notes for a specific issue (Issue #412, #413)
app.MapGet("/api/issues/{id}/notes", async (string id, IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    var issues = await GetAllIssuesAsync(config, cache);
    var issue = issues.FirstOrDefault(i => i.Id == id);
    if (issue == null) return Results.NotFound($"Issue '{id}' not found.");

    return Results.Ok(new { notes = issue.Notes ?? string.Empty });
});

// API: Issues – Update notes for a specific issue (Issue #412, #413)
app.MapPatch("/api/issues/{id}/notes", async (string id, [FromBody] UpdateNotesRequest req, IConfiguration config, Microsoft.Extensions.Caching.Memory.IMemoryCache cache) =>
{
    if (string.IsNullOrWhiteSpace(id)) return Results.BadRequest(new { error = "Issue ID is required" });

    try
    {
        // Find which environment this issue belongs to
        var envs = await GetConfiguredEnvironmentsAsync();
        Abo.Core.Connectors.IIssueTrackerConnector? tracker = null;
        string? targetEnvName = null;

        foreach (var env in envs.Where(e => e.IssueTracker != null))
        {
            if (env.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
            {
                var testTracker = new Abo.Integrations.GitHub.GitHubIssueTrackerConnector(env.IssueTracker, config["Integrations:GitHub:Token"], env.Name);
                try
                {
                    var testIssue = await testTracker.GetIssueAsync(id);
                    if (testIssue != null)
                    {
                        tracker = testTracker;
                        targetEnvName = env.Name;
                        break;
                    }
                }
                catch { continue; }
            }
            else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
            {
                var testTracker = new Abo.Core.Connectors.FileSystemIssueTrackerConnector(env.Name);
                try
                {
                    var testIssue = await testTracker.GetIssueAsync(id);
                    if (testIssue != null)
                    {
                        tracker = testTracker;
                        targetEnvName = env.Name;
                        break;
                    }
                }
                catch { continue; }
            }
        }

        if (tracker == null)
        {
            return Results.NotFound($"Issue '{id}' not found in any configured environment.");
        }

        // Update the notes using the notes parameter
        await tracker.UpdateIssueAsync(id, notes: req.Notes);

        // Invalidate cache
        cache.Remove("AllActiveIssues");

        return Results.Ok(new { notes = req.Notes ?? string.Empty });
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error updating notes for issue {IssueId}", id);
        return Results.Problem($"Error updating notes: {ex.Message}");
    }
});

// API: Issues – Get full issue details including notes (Issue #414)
app.MapGet("/api/issues/{id}/details", async (string id, IConfiguration config) =>
{
    var envs = await GetConfiguredEnvironmentsAsync();

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
                var issue = await tracker.GetIssueAsync(id, includeDetails: true);
                if (issue != null)
                {
                    return Results.Ok(new
                    {
                        Id = issue.Id,
                        Title = issue.Title,
                        Body = issue.Body,
                        State = issue.State,
                        Project = issue.Project,
                        Status = issue.Status,
                        Type = issue.Type,
                        Labels = issue.Labels,
                        Notes = issue.Notes ?? string.Empty,
                        Comments = issue.Comments
                    });
                }
            }
            catch { continue; }
        }
    }

    return Results.NotFound($"Issue '{id}' not found.");
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

    // Initialize Auth Service - create CEO user if not exists
    Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<Abo.Core.Services.AuthService>();
            var mattermostClient = scope.ServiceProvider.GetRequiredService<MattermostClient>();
            var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<MattermostOptions>>().Value;

            await authService.InitializeAsync(mattermostClient, options.CeoUserName);
        }
        catch (Exception ex)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Failed to initialize Auth Service (CEO user creation)");
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

// ── Request/Response DTOs ─────────────────────────────────────────────────────

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class InitPasswordRequest
{
    public string Username { get; set; } = string.Empty;
    public List<string>? Roles { get; set; }
    public string? MattermostUsername { get; set; }
}

public class ResetPasswordRequest
{
    public string? MattermostUsername { get; set; }
}

public class CreateIssueRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string? Project { get; set; }
    public string? EnvironmentName { get; set; }
}

public class UpdateNotesRequest
{
    public string Notes { get; set; } = string.Empty;
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

// ── Background Services ───────────────────────────────────────────────────────

/// <summary>
/// Background service that periodically cleans up expired auth sessions.
/// </summary>
public class AuthCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AuthCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(15);

    public AuthCleanupService(IServiceProvider services, ILogger<AuthCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Auth Cleanup Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var authService = scope.ServiceProvider.GetRequiredService<Abo.Core.Services.AuthService>();
                await authService.CleanupExpiredSessionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auth session cleanup");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }
    }
}
