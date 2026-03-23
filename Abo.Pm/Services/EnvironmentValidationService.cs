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
    private readonly StartupStatusService _startupStatus;

    public EnvironmentValidationService(
        ILogger<EnvironmentValidationService> logger, 
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        StartupStatusService startupStatus)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _startupStatus = startupStatus;
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
                        var err = $"Directory: '{env.Dir}' NOT FOUND or INACCESSIBLE.";
                        _logger.LogError($"  [ERROR] {err}");
                        _startupStatus.AddError(err);
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
                        {
                            var err = $"Wiki (Filesystem): '{fullPath}' NOT FOUND.";
                            _logger.LogError($"  [ERROR] {err}");
                            _startupStatus.AddError(err);
                        }
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
                            var err = $"Wiki (GitHub): 'git' CLI is NOT installed or accessible.";
                            _logger.LogError($"  [ERROR] {err}");
                            _startupStatus.AddError(err);
                        }
                        
                        var parts = env.Wiki.RootPath.Split('/');
                        if (parts.Length == 2)
                        {
                            var fakeConfig = new IssueTrackerConfig { Owner = parts[0], Repository = parts[1] };
                            await CheckGitHubAsync(fakeConfig, cancellationToken);
                        }
                        else
                        {
                            var err = $"Wiki (GitHub): RootPath must be in 'owner/repo' format.";
                            _logger.LogError($"  [ERROR] {err}");
                            _startupStatus.AddError(err);
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
            var err = $"Wiki (XpectoLive): Failed to reach Space '{spaceId}'. Message: {ex.Message}";
            _logger.LogError($"  [ERROR] {err}");
            _startupStatus.AddError(err);
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
                 var err = $"IssueTracker (GitHub): Repo '{issueTracker.Owner}/{issueTracker.Repository}' is {(response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "UNAUTHORIZED" : "NOT FOUND")}.";
                 _logger.LogError($"  [ERROR] {err}");
                 _startupStatus.AddError(err);
            }
            else
            {
                 var err = $"IssueTracker (GitHub): Unexpected status {(int)response.StatusCode} for '{issueTracker.Owner}/{issueTracker.Repository}'.";
                 _logger.LogWarning($"  [WARN] {err}");
                 _startupStatus.AddError(err);
            }

            if (issueTracker.ProjectTitles != null && issueTracker.ProjectTitles.Any())
            {
                try
                {
                    var graphqlUrl = "https://api.github.com/graphql";
                    var query = @"query($owner: String!) { organization(login: $owner) { projectsV2(first: 20) { nodes { title } } } }";
                    var payload = new { query, variables = new { owner = issueTracker.Owner } };
                    var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                    var req = new HttpRequestMessage(HttpMethod.Post, graphqlUrl) { Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json") };
                    var gqlResponse = await client.SendAsync(req, cancellationToken);
                    var gqlJson = await gqlResponse.Content.ReadAsStringAsync();
                    
                    if (gqlResponse.IsSuccessStatusCode)
                    {
                        var doc = JsonDocument.Parse(gqlJson);
                        if (doc.RootElement.TryGetProperty("errors", out var errorsList) && errorsList.ValueKind == JsonValueKind.Array) {
                            var firstErr = errorsList.EnumerateArray().FirstOrDefault().GetProperty("message").GetString();
                            var err = $"IssueTracker (GitHub): Token rejected project access. API Message: {firstErr}";
                            _logger.LogError($"  [ERROR] {err}");
                            _startupStatus.AddError(err);
                        }

                        JsonElement? orgNode = null;
                        if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("organization", out var org) && org.ValueKind != JsonValueKind.Null) {
                            orgNode = org;
                        } else if (!doc.RootElement.TryGetProperty("errors", out _)) {
                            var userQuery = query.Replace("organization(login: $owner)", "user(login: $owner)");
                            var uPayload = new { query = userQuery, variables = new { owner = issueTracker.Owner } };
                            var uJsonPayload = JsonSerializer.Serialize(uPayload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                            var uReq = new HttpRequestMessage(HttpMethod.Post, graphqlUrl) { Content = new StringContent(uJsonPayload, System.Text.Encoding.UTF8, "application/json") };
                            var uResp = await client.SendAsync(uReq, cancellationToken);
                            var uJson = await uResp.Content.ReadAsStringAsync();
                            var uDoc = JsonDocument.Parse(uJson);
                            
                            if (uDoc.RootElement.TryGetProperty("errors", out var uErrorsList) && uErrorsList.ValueKind == JsonValueKind.Array) {
                                var firstErr = uErrorsList.EnumerateArray().FirstOrDefault().GetProperty("message").GetString();
                                var err = $"IssueTracker (GitHub): Token rejected user project access. API Message: {firstErr}";
                                _logger.LogError($"  [ERROR] {err}");
                                _startupStatus.AddError(err);
                            }

                            if (uDoc.RootElement.TryGetProperty("data", out var uData) && uData.TryGetProperty("user", out var usr) && usr.ValueKind != JsonValueKind.Null) {
                                orgNode = usr;
                            }
                        }

                        if (orgNode != null && orgNode.Value.TryGetProperty("projectsV2", out var pV2) && pV2.ValueKind != JsonValueKind.Null && pV2.TryGetProperty("nodes", out var pNodes) && pNodes.ValueKind != JsonValueKind.Null) {
                            var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach(var pNode in pNodes.EnumerateArray()) {
                                if (pNode.ValueKind != JsonValueKind.Null && pNode.TryGetProperty("title", out var tProp) && tProp.ValueKind != JsonValueKind.Null) {
                                    var pTitle = tProp.GetString();
                                    if (pTitle != null) existingTitles.Add(pTitle);
                                }
                            }

                            foreach(var expected in issueTracker.ProjectTitles.Values) {
                                if (!existingTitles.Contains(expected)) {
                                    var errMsg = $"IssueTracker (GitHub): Expected Project '{expected}' was NOT FOUND.";
                                    _logger.LogError($"  [ERROR] {errMsg}");
                                    _startupStatus.AddError(errMsg);
                                } else {
                                    _logger.LogInformation($"  [OK] IssueTracker (GitHub): Project '{expected}' natively verified.");
                                }
                            }
                        }
                    }
                } catch (Exception ex) {
                    var err = $"IssueTracker (GitHub): Error executing GraphQL project verification. Details: {ex.Message}";
                    _logger.LogError(ex, $"  [ERROR] {err}");
                    _startupStatus.AddError(err);
                }
            }
        }
        catch (Exception ex)
        {
            var err = $"IssueTracker (GitHub): Failed to reach repo '{issueTracker.Owner}/{issueTracker.Repository}'. Message: {ex.Message}";
            _logger.LogError($"  [ERROR] {err}");
            _startupStatus.AddError(err);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
