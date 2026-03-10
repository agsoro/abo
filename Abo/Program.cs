using Abo.Agents;
using Abo.Core;
using Abo.Integrations.Mattermost;
using Abo.Integrations.XpectoLive;
using Abo.Tools;
using Abo.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Register Core Components
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<UserService>();
builder.Services.AddTransient<Orchestrator>();
builder.Services.AddTransient<AgentSupervisor>();

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

// Register Agents
builder.Services.AddTransient<HelloWorldAgent>();
builder.Services.AddTransient<IAgent, HelloWorldAgent>(sp => sp.GetRequiredService<HelloWorldAgent>());
builder.Services.AddTransient<QuizAgent>();
builder.Services.AddTransient<IAgent, QuizAgent>(sp => sp.GetRequiredService<QuizAgent>());

// Register Background Services
builder.Services.AddHostedService<QuizService>();

var app = builder.Build();

app.UseDefaultFiles(); // Add this line so `/` automatically serves `/index.html`
app.UseStaticFiles(); // Serve wwwroot/index.html

// API: Health / Status
app.MapGet("/api/status", (IConfiguration config) =>
{
    var endpoint = config["Config:ApiEndpoint"];
    var model = config["Config:ModelName"];
    var hasKey = !string.IsNullOrEmpty(config["Config:ApiKey"]);
    return Results.Ok(new { Status = "Running", Model = model, HasApiKey = hasKey });
});

// API: Interact
app.MapPost("/api/interact", async ([FromBody] InteractRequest req, Orchestrator orchestrator, AgentSupervisor supervisor, UserService userService) =>
{
    if (string.IsNullOrWhiteSpace(req.Message)) return Results.BadRequest("Message is empty.");

    var sessionId = req.SessionId ?? "web-session";
    var userName = req.UserName ?? "Web User";
    var userId = req.UserId ?? sessionId;

    userService.GetOrCreateUser(userId, userName);

    var history = orchestrator.GetSessionHistory(sessionId);
    var agent = await supervisor.GetBestAgentAsync(req.Message, history);
    var response = await orchestrator.RunAgentLoopAsync(agent, req.Message, sessionId, req.UserName, userId);

    return Results.Ok(new { Output = response });
});

app.Run();

public class InteractRequest
{
    public string Message { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
}

