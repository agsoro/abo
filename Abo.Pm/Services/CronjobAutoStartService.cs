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
/// Background service that triggers every 10 minutes (aligned to UTC grid: :00, :10, :20, …) to:
///   1. Fetch all open issues.
///   2. Send a Mattermost DM to the CEO listing the open work it intends to process.
///   3. Invoke the ManagerAgent via the Orchestrator to start processing that work.
///
/// A <see cref="SemaphoreSlim"/> concurrency guard ensures that if a previous cycle is still
/// running when the next tick fires, the new tick is skipped (logged as a warning) to prevent
/// parallel development phases on a single environment.
///
/// Can be disabled via Config:CronjobEnabled = false in appsettings.json.
/// </summary>
public class CronjobAutoStartService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CronjobAutoStartService> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Ensures only one <see cref="RunCycleAsync"/> execution is active at a time.
    /// </summary>
    private readonly SemaphoreSlim _cycleSemaphore = new SemaphoreSlim(1, 1);

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

        _logger.LogInformation("CronjobAutoStartService started. Will trigger every 10 minutes (UTC, aligned to grid).");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextTenMinuteMark();

            // Lower-bound clamp: Task.Delay throws ArgumentOutOfRangeException for non-positive values.
            // This can happen due to millisecond-level precision edge cases when the loop re-enters
            // exactly on (or just after) a 10-minute grid boundary.
            if (delay <= TimeSpan.Zero)
            {
                _logger.LogWarning(
                    "CronjobAutoStartService: Computed delay was non-positive ({Delay}); clamping to 100 ms.",
                    delay);
                delay = TimeSpan.FromMilliseconds(100);
            }
            // Upper-bound clamp (defence-in-depth): guard against a corrupt or extreme system clock
            // returning a value that would exceed Task.Delay's maximum allowed timer duration.
            else if (delay > TimeSpan.FromMinutes(10))
            {
                _logger.LogWarning(
                    "CronjobAutoStartService: Computed delay exceeded 10 minutes ({Delay}); clamping to 10 min.",
                    delay);
                delay = TimeSpan.FromMinutes(10);
            }

            _logger.LogInformation("CronjobAutoStartService: next trigger in {Delay:mm\\:ss}.", delay);

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

            // Concurrency guard: skip this tick if the previous cycle is still running
            if (!await _cycleSemaphore.WaitAsync(TimeSpan.Zero, stoppingToken))
            {
                _logger.LogWarning("CronjobAutoStartService: Previous cycle still running — skipping tick at {SkipTime:O}.", DateTime.UtcNow);
                continue;
            }

            try
            {
                await RunCycleAsync(stoppingToken);
            }
            finally
            {
                _cycleSemaphore.Release();
            }
        }

        _logger.LogInformation("CronjobAutoStartService stopped.");
    }

    /// <summary>
    /// Calculates the time span until the next 10-minute grid mark (UTC).
    /// Grid marks are :00, :10, :20, :30, :40, :50 of every hour.
    /// </summary>
    private static TimeSpan TimeUntilNextTenMinuteMark()
    {
        var now = DateTime.UtcNow;
        var minutesIntoCurrentBlock = now.Minute % 10;
        var nextMark = now.Date
            .AddHours(now.Hour)
            .AddMinutes(now.Minute - minutesIntoCurrentBlock + 10)
            .AddSeconds(-now.Second)
            .AddMilliseconds(-now.Millisecond);
        return nextMark - now;
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
                        "IMPORTANT: Prioritize newly triaged issues (step: 'open') first. " +
                        "When no 'open' issues remain, pick the in-progress issue closest to completion: prefer 'review' > 'check' > 'work' > 'planned'. " +
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
