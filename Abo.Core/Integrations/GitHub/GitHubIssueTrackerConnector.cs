using System.Text;
using System.Text.Json;
using Abo.Core.Connectors;
using Abo.Contracts.Models;

namespace Abo.Integrations.GitHub;

public class GitHubIssueTrackerConnector : IIssueTrackerConnector
{
    private readonly IssueTrackerConfig _config;
    private readonly string? _issueTrackerToken;
    private readonly string? _environmentName;
    private static readonly HttpClient _sharedHttpClient = new HttpClient();
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    public GitHubIssueTrackerConnector(IssueTrackerConfig config, string? issueTrackerToken = null, string? environmentName = null)
    {
        _config = config;
        _issueTrackerToken = issueTrackerToken;
        _environmentName = environmentName;
    }

    private HttpRequestMessage CreateGitHubRequest(HttpMethod method, string path, object? body = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_config.BaseUrl) ? "https://api.github.com" : _config.BaseUrl;
        var url = $"{baseUrl}/repos/{_config.Owner}/{_config.Repository}/{path.TrimStart('/')}";

        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Accept", "application/vnd.github.v3+json");
        req.Headers.Add("User-Agent", "Abo-Agent");
        if (!string.IsNullOrWhiteSpace(_issueTrackerToken))
        {
            req.Headers.Add("Authorization", $"Bearer {_issueTrackerToken}");
        }

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return req;
    }

    private async Task<string> SendGitHubRequestAsync(HttpRequestMessage req)
    {
        if (string.IsNullOrWhiteSpace(_issueTrackerToken))
            throw new Exception("Error: GitHub token is missing from global configuration (Integrations:GitHub:Token).");

        using var response = await _sharedHttpClient.SendAsync(req);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"GitHub API Error ({(int)response.StatusCode}): {response.ReasonPhrase}\n{content}");
        }
        return content;
    }

    public async Task<IEnumerable<IssueRecord>> ListIssuesAsync(string? state = null, string[]? labels = null)
    {
        var path = "issues?per_page=30";
        if (!string.IsNullOrWhiteSpace(state)) path += $"&state={Uri.EscapeDataString(state)}";
        if (labels != null && labels.Any()) path += $"&labels={Uri.EscapeDataString(string.Join(",", labels))}";

        using var req = CreateGitHubRequest(HttpMethod.Get, path);
        var json = await SendGitHubRequestAsync(req);
        var ghIssues = JsonSerializer.Deserialize<List<GitHubIssue>>(json, _jsonOptions);
        return ghIssues?.Select(i => i.ToRecord(_environmentName)) ?? Enumerable.Empty<IssueRecord>();
    }

    public async Task<IssueRecord?> GetIssueAsync(string issueId)
    {
        using var req = CreateGitHubRequest(HttpMethod.Get, $"issues/{Uri.EscapeDataString(issueId)}");
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        return ghIssue?.ToRecord(_environmentName);
    }

    public async Task<IssueRecord> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null)
    {
        var labelsList = new List<string>();
        if (!string.IsNullOrWhiteSpace(type)) labelsList.Add($"type: {type}");
        if (!string.IsNullOrWhiteSpace(size)) labelsList.Add($"size: {size}");
        if (additionalLabels != null) labelsList.AddRange(additionalLabels);

        var reqObj = new
        {
            title,
            body,
            labels = labelsList.Any() ? labelsList : null
        };

        using var req = CreateGitHubRequest(HttpMethod.Post, "issues", reqObj);
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        return ghIssue?.ToRecord(_environmentName) ?? new IssueRecord();
    }

    public async Task<IssueRecord> UpdateIssueAsync(string issueId, string? title = null, string? body = null, string? state = null, string[]? labels = null)
    {
        var reqObj = new Dictionary<string, object>();
        if (title != null) reqObj["title"] = title;
        if (body != null) reqObj["body"] = body;
        if (state != null) reqObj["state"] = state;
        if (labels != null) reqObj["labels"] = labels;

        using var req = CreateGitHubRequest(HttpMethod.Patch, $"issues/{Uri.EscapeDataString(issueId)}", reqObj);
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        return ghIssue?.ToRecord(_environmentName) ?? new IssueRecord();
    }

    public async Task<string> AddIssueCommentAsync(string issueId, string body)
    {
        using var req = CreateGitHubRequest(HttpMethod.Post, $"issues/{Uri.EscapeDataString(issueId)}/comments", new { body });
        return await SendGitHubRequestAsync(req);
    }

    private class GitHubLabel { public string name { get; set; } = string.Empty; }
    private class GitHubIssue
    {
        public int number { get; set; }
        public string title { get; set; } = string.Empty;
        public string body { get; set; } = string.Empty;
        public string state { get; set; } = string.Empty;
        public List<GitHubLabel> labels { get; set; } = new();

        public IssueRecord ToRecord(string? envName)
        {
            var labelsList = labels?.Select(l => l.name).ToList() ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(envName))
            {
                labelsList.RemoveAll(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase));
                labelsList.Add($"env: {envName}");
            }
            
            return new IssueRecord
            {
                Id = number.ToString(),
                Title = title ?? string.Empty,
                Body = body ?? string.Empty,
                State = state ?? string.Empty,
                Labels = labelsList
            };
        }
    }
}
