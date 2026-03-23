using Abo.Core.Connectors;
using Abo.Integrations.XpectoLive;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;

namespace Abo.Services;

public class EnvironmentValidationService : IHostedService
{
    private readonly ILogger<EnvironmentValidationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public EnvironmentValidationService(
        ILogger<EnvironmentValidationService> logger, 
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("================== STARTUP ENVIRONMENT CHECKS ==================");

        var envFile = Path.Combine(AppContext.BaseDirectory, "Data", "Environments", "environments.json");
        if (!File.Exists(envFile))
        {
            _logger.LogWarning($"[WARN] Environments configuration not found at {envFile}");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(envFile, cancellationToken);
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var environments = JsonSerializer.Deserialize<List<ConnectorEnvironment>>(json, jsOptions);

            if (environments == null || !environments.Any())
            {
                _logger.LogWarning("[WARN] No environments configured.");
                return;
            }

            foreach (var env in environments)
            {
                _logger.LogInformation($"Checking Environment: {env.Name}");

                // 1. Directory Check
                if (!string.IsNullOrWhiteSpace(env.Dir))
                {
                    if (Directory.Exists(env.Dir))
                    {
                        _logger.LogInformation($"  [OK] Directory: '{env.Dir}' is accessible.");
                    }
                    else
                    {
                        _logger.LogError($"  [ERROR] Directory: '{env.Dir}' NOT FOUND or INACCESSIBLE.");
                    }
                }
                else
                {
                    _logger.LogWarning("  [WARN] Environment has no Dir configured.");
                }

                // 2. Wiki Check
                if (env.Wiki != null)
                {
                    if (env.Wiki.Type.Equals("filesystem", StringComparison.OrdinalIgnoreCase))
                    {
                        var fullPath = Path.Combine(env.Dir, env.Wiki.RootPath.TrimStart('\\', '/'));
                        if (Directory.Exists(fullPath))
                            _logger.LogInformation($"  [OK] Wiki (Filesystem): '{fullPath}' is accessible.");
                        else
                            _logger.LogError($"  [ERROR] Wiki (Filesystem): '{fullPath}' NOT FOUND.");
                    }
                    else if (env.Wiki.Type.Equals("xpectolive", StringComparison.OrdinalIgnoreCase))
                    {
                        await CheckXpectoLiveAsync(env.Wiki.RootPath, cancellationToken);
                    }
                    else if (env.Wiki.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                    {
                        try {
                            var processInfo = new System.Diagnostics.ProcessStartInfo { FileName = "git", Arguments = "--version", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                            using var process = System.Diagnostics.Process.Start(processInfo);
                            process?.WaitForExit();
                            _logger.LogInformation($"  [OK] Wiki (GitHub): 'git' CLI is available.");
                        } catch {
                            _logger.LogError($"  [ERROR] Wiki (GitHub): 'git' CLI is NOT installed or accessible.");
                        }
                        
                        var parts = env.Wiki.RootPath.Split('/');
                        if (parts.Length == 2)
                        {
                            var fakeConfig = new IssueTrackerConfig { Owner = parts[0], Repository = parts[1] };
                            await CheckGitHubAsync(fakeConfig, cancellationToken);
                        }
                        else
                        {
                            _logger.LogError($"  [ERROR] Wiki (GitHub): RootPath must be in 'owner/repo' format.");
                        }
                    }
                }

                // 3. Issue Tracker Check
                if (env.IssueTracker != null)
                {
                    if (env.IssueTracker.Type.Equals("github", StringComparison.OrdinalIgnoreCase))
                    {
                        await CheckGitHubAsync(env.IssueTracker, cancellationToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ERROR] Error validating environments.");
        }
        finally
        {
            _logger.LogInformation("================================================================");
        }
    }

    private async Task CheckXpectoLiveAsync(string spaceId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var wikiClient = scope.ServiceProvider.GetRequiredService<IXpectoLiveWikiClient>();
            
            var space = await wikiClient.GetSpaceAsync(spaceId);
            if (space != null)
            {
                _logger.LogInformation($"  [OK] Wiki (XpectoLive): Space '{spaceId}' is reachable.");
            }
            else
            {
                _logger.LogWarning($"  [WARN] Wiki (XpectoLive): Reachable but Space '{spaceId}' might not exist.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"  [ERROR] Wiki (XpectoLive): Failed to reach Space '{spaceId}'. Message: {ex.Message}");
        }
    }

    private async Task CheckGitHubAsync(IssueTrackerConfig issueTracker, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(issueTracker.BaseUrl) ? "https://api.github.com" : issueTracker.BaseUrl;
            var url = $"{baseUrl}/repos/{issueTracker.Owner}/{issueTracker.Repository}";

            var client = _httpClientFactory.CreateClient("GitHubCheck");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            
            var userAgent = _configuration["Integrations:GitHub:UserAgent"] ?? "Abo-Agent";
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);

            var token = _configuration["Integrations:GitHub:Token"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await client.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                 _logger.LogInformation($"  [OK] IssueTracker (GitHub): Repo '{issueTracker.Owner}/{issueTracker.Repository}' is accessible.");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                 _logger.LogError($"  [ERROR] IssueTracker (GitHub): Repo '{issueTracker.Owner}/{issueTracker.Repository}' is {(response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "UNAUTHORIZED" : "NOT FOUND")}.");
            }
            else
            {
                 _logger.LogWarning($"  [WARN] IssueTracker (GitHub): Unexpected status {(int)response.StatusCode} for '{issueTracker.Owner}/{issueTracker.Repository}'.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"  [ERROR] IssueTracker (GitHub): Failed to reach repo '{issueTracker.Owner}/{issueTracker.Repository}'. Message: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
