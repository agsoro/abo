using System.Text;
using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Integrations.GitHub;

public class GitHubIssueTrackerConnector : IIssueTrackerConnector
{
    private readonly IssueTrackerConfig _config;
    private readonly string? _issueTrackerToken;
    private static readonly HttpClient _sharedHttpClient = new HttpClient();

    public GitHubIssueTrackerConnector(IssueTrackerConfig config, string? issueTrackerToken = null)
    {
        _config = config;
        _issueTrackerToken = issueTrackerToken;
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
            return "Error: GitHub token is missing from global configuration (Integrations:GitHub:Token).";

        using var response = await _sharedHttpClient.SendAsync(req);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return $"GitHub API Error ({(int)response.StatusCode}): {response.ReasonPhrase}\n{content}";
        }
        return content;
    }

    public async Task<string> ListIssuesAsync(string? state = null, string[]? labels = null)
    {
        try
        {
            var path = "issues?per_page=30";
            if (!string.IsNullOrWhiteSpace(state)) path += $"&state={Uri.EscapeDataString(state)}";
            if (labels != null && labels.Any()) path += $"&labels={Uri.EscapeDataString(string.Join(",", labels))}";

            using var req = CreateGitHubRequest(HttpMethod.Get, path);
            return await SendGitHubRequestAsync(req);
        }
        catch (Exception ex) { return $"Error listing issues: {ex.Message}"; }
    }

    public async Task<string> GetIssueAsync(string issueId)
    {
        try
        {
            using var req = CreateGitHubRequest(HttpMethod.Get, $"issues/{Uri.EscapeDataString(issueId)}");
            return await SendGitHubRequestAsync(req);
        }
        catch (Exception ex) { return $"Error getting issue: {ex.Message}"; }
    }

    public async Task<string> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null)
    {
        try
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
            return await SendGitHubRequestAsync(req);
        }
        catch (Exception ex) { return $"Error creating issue: {ex.Message}"; }
    }

    public async Task<string> AddIssueCommentAsync(string issueId, string body)
    {
        try
        {
            using var req = CreateGitHubRequest(HttpMethod.Post, $"issues/{Uri.EscapeDataString(issueId)}/comments", new { body });
            return await SendGitHubRequestAsync(req);
        }
        catch (Exception ex) { return $"Error adding issue comment: {ex.Message}"; }
    }
}
