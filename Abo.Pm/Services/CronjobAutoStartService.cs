using System.Text;
using System.Text.Json;
using Abo.Agents;
using Abo.Core;
using Abo.Core.Connectors;
using Abo.Integrations.GitHub;
using Abo.Integrations.Mattermost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abo.Services;

/// <summary>
/// Background service that triggers on every full hour to:
///   1. Fetch all open issues.
///   2. Send a Mattermost DM to the CEO listing the open work it intends to process.
///   3. Invoke the ManagerAgent via the Orchestrator to start processing that work.
///
/// Can be disabled via Config:CronjobEnabled = false in appsettings.json.
/// </summary>
public class CronjobAutoStartService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CronjobAutoStartService> _logger;
    private readonly IConfiguration _configuration;

    public CronjobAutoStartService(
        IServiceProvider serviceProvider,
        ILogger<CronjobAutoStartService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool?>("Config:CronjobEnabled");
        if (enabled != true)
        {
            _logger.LogInformation("CronjobAutoStartService is disabled (Config:CronjobEnabled is not true). Exiting.");
            return;
        }

        _logger.LogInformation("CronjobAutoStartService started. Will trigger on every full hour (UTC).");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextFullHour();
            _logger.LogInformation("CronjobAutoStartService: next trigger in {Delay:hh\\:mm\\:ss}.", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // App is shutting down — exit cleanly
                break;
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            await RunCycleAsync(stoppingToken);
        }

        _logger.LogInformation("CronjobAutoStartService stopped.");
    }

    /// <summary>
    /// Calculates the time span until the next full UTC hour.
    /// </summary>
    private static TimeSpan TimeUntilNextFullHour()
    {
        var now = DateTime.UtcNow;
        var nextHour = now.Date.AddHours(now.Hour + 1);
        return nextHour - now;
    }

    /// <summary>
    /// Executes a single cronjob cycle:
    ///   - Fetches open issues
    ///   - Notifies CEO via Mattermost DM
    ///   - Invokes ManagerAgent if there is open work
    /// All exceptions are caught to ensure the background loop is never terminated by a single failed cycle.
    /// </summary>
    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var triggerTime = DateTime.UtcNow;
        _logger.LogInformation("CronjobAutoStartService: Running cycle at {TriggerTime:O}.", triggerTime);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var mattermostClient = scope.ServiceProvider.GetRequiredService<MattermostClient>();
            var mattermostOptions = scope.ServiceProvider.GetRequiredService<IOptions<MattermostOptions>>().Value;
            var orchestrator = scope.ServiceProvider.GetRequiredService<Orchestrator>();
            var managerAgent = scope.ServiceProvider.GetRequiredService<ManagerAgent>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // 1. Fetch open issues
            var issues = await GetAllIssuesAsync(config);
            var openIssues = issues.Where(i => !string.Equals(i.State, "closed", StringComparison.OrdinalIgnoreCase)).ToList();

            // 2. Build CEO notification message
            var sb = new StringBuilder();
            sb.AppendLine($"⏰ Cronjob triggered at {triggerTime:yyyy-MM-dd HH:mm} UTC.");
            sb.AppendLine();

            if (openIssues.Any())
            {
                sb.AppendLine("**📋 Starting work on open issues:**");
                var byProject = openIssues.GroupBy(i => string.IsNullOrWhiteSpace(i.Project) ? "Unassigned" : i.Project);
                foreach (var group in byProject)
                {
                    sb.AppendLine();
                    sb.AppendLine($"*{group.Key}*:");
                    foreach (var issue in group)
                    {
                        var step = Abo.Core.WorkflowEngine.ResolveStepIdFallback(issue);
                        var role = Abo.Core.WorkflowEngine.GetStepInfo(step)?.RequiredRole ?? "Any";
                        var envName = issue.Labels.FirstOrDefault(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase))?.Substring(5).Trim() ?? "?";
                        sb.AppendLine($"- [{issue.Id}] {issue.Title} (Env: {envName}, Step: {step}, Role: {role})");
                    }
                }
            }
            else
            {
                sb.AppendLine("No open work found — skipping ManagerAgent invocation.");
            }

            // 3. Send DM to CEO
            if (!string.IsNullOrEmpty(mattermostOptions.CeoUserName))
            {
                try
                {
                    await mattermostClient.SendDirectMessageAsync(mattermostOptions.CeoUserName, sb.ToString());
                    _logger.LogInformation("CronjobAutoStartService: CEO notification sent.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CronjobAutoStartService: Failed to send CEO notification via Mattermost.");
                }
            }
            else
            {
                _logger.LogWarning("CronjobAutoStartService: CeoUserName is not configured — skipping Mattermost notification.");
            }

            // 4. Invoke ManagerAgent if there are open issues
            if (openIssues.Any())
            {
                var subSessionId = $"cronjob-{triggerTime:yyyyMMddHHmm}";
                _logger.LogInformation("CronjobAutoStartService: Invoking ManagerAgent (session: {SessionId}).", subSessionId);

                try
                {
                    var initialMessage =
                        "Start working on open issues now. " +
                        "IMPORTANT: Prioritize completing any issue that is already in-progress " +
                        "(steps: review, check, work, or planned) before picking up a new issue at step 'open'. " +
                        "Pick the highest-priority issue from `get_open_work` and delegate it.";

                    await orchestrator.RunAgentLoopAsync(
                        managerAgent,
                        initialMessage,
                        subSessionId,
                        "CronjobAutoStartService");

                    _logger.LogInformation("CronjobAutoStartService: ManagerAgent cycle completed (session: {SessionId}).", subSessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CronjobAutoStartService: ManagerAgent invocation failed for session {SessionId}.", subSessionId);
                }
            }
            else
            {
                _logger.LogInformation("CronjobAutoStartService: No open issues found — ManagerAgent not invoked.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CronjobAutoStartService: Unhandled exception during cycle at {TriggerTime:O}.", triggerTime);
        }
    }

    /// <summary>
    /// Fetches all issues from all configured environments.
    /// Mirrors the GetAllIssuesAsync helper in Program.cs.
    /// </summary>
    private static async Task<List<Abo.Contracts.Models.IssueRecord>> GetAllIssuesAsync(IConfiguration config)
    {
        var environmentsFile = Path.Combine(AppContext.BaseDirectory, "Data", "Environments", "environments.json");
        var envs = new List<ConnectorEnvironment>();

        if (File.Exists(environmentsFile))
        {
            var envJson = await File.ReadAllTextAsync(environmentsFile);
            envs = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(envJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }

        var activeIssues = new List<Abo.Contracts.Models.IssueRecord>();

        foreach (var env in envs.Where(e => e.IssueTracker != null))
        {
            IIssueTrackerConnector? tracker = null;

            if (env.IssueTracker!.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
            {
                tracker = new GitHubIssueTrackerConnector(env.IssueTracker, config["Integrations:GitHub:Token"], env.Name);
            }
            else if (env.IssueTracker.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
            {
                tracker = new FileSystemIssueTrackerConnector(env.Name);
            }

            if (tracker != null)
            {
                try
                {
                    var issues = await tracker.ListIssuesAsync();
                    foreach (var issue in issues)
                    {
                        if (!activeIssues.Any(i => i.Id == issue.Id && i.Title == issue.Title))
                            activeIssues.Add(issue);
                    }
                }
                catch
                {
                    /* Ignore individual tracker errors */
                }
            }
        }

        return activeIssues;
    }
}
