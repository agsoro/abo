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
    
    private static readonly Dictionary<string, string> _projectNodeIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, (string fieldId, string optionId)>> _projectStatuses = new(StringComparer.OrdinalIgnoreCase);

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

    private async Task<string> SendGraphQLRequestAsync(object payload, bool throwGraphQLErrors = false)
    {
        if (string.IsNullOrWhiteSpace(_issueTrackerToken))
            throw new Exception("Error: GitHub token is missing from global configuration (Integrations:GitHub:Token).");

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql");
        req.Headers.Add("User-Agent", "Abo-Agent");
        req.Headers.Add("Authorization", $"Bearer {_issueTrackerToken}");
        
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        
        using var response = await _sharedHttpClient.SendAsync(req);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"GitHub GraphQL API Error ({(int)response.StatusCode}): {response.ReasonPhrase}\n{content}");
        }

        if (throwGraphQLErrors && content.Contains("\"errors\":")) {
            using var doc = JsonDocument.Parse(content);
            if (throwGraphQLErrors && doc.RootElement.TryGetProperty("errors", out var errorsProp)) {
                if (errorsProp.ValueKind == JsonValueKind.Array && errorsProp.GetArrayLength() > 0)
                {
                    var firstError = errorsProp[0].GetProperty("message").GetString();
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[GitHub Connector Error] GraphQL Mapping Error: {firstError}");
                    Console.ResetColor();
                    throw new Exception("GraphQL Mapping Error: " + firstError);
                }
            }
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
        var records = ghIssues?.Select(i => i.ToRecord(_environmentName)).ToList() ?? new List<IssueRecord>();
        await EnrichIssuesWithProjectFieldsAsync(records);
        return records;
    }

    public async Task<IssueRecord?> GetIssueAsync(string issueId)
    {
        using var req = CreateGitHubRequest(HttpMethod.Get, $"issues/{Uri.EscapeDataString(issueId)}");
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        var record = ghIssue?.ToRecord(_environmentName);
        if (record != null) await EnrichIssuesWithProjectFieldsAsync(new List<IssueRecord> { record });
        return record;
    }

    public async Task<IssueRecord> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null, string? project = null, string? stepId = null)
    {
        var labelsList = new List<string>();
        if (!string.IsNullOrWhiteSpace(type)) labelsList.Add($"type: {type}");
        if (!string.IsNullOrWhiteSpace(size)) labelsList.Add($"size: {size}");
        if (additionalLabels != null) labelsList.AddRange(additionalLabels);
        if (!string.IsNullOrWhiteSpace(project)) labelsList.Add($"project: {project}");

        var restLabels = labelsList.Where(l => !l.StartsWith("project: ", StringComparison.OrdinalIgnoreCase)).ToList();

        var reqObj = new
        {
            title,
            body,
            labels = restLabels.Any() ? restLabels : null
        };

        using var req = CreateGitHubRequest(HttpMethod.Post, "issues", reqObj);
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        var record = ghIssue?.ToRecord(_environmentName) ?? new IssueRecord();
        
        if (!string.IsNullOrWhiteSpace(record.NodeId)) {
            record.Project = project ?? string.Empty;
            record.StepId = stepId ?? string.Empty;
            await SyncProjectV2Async(record);
        }
        return record;
    }

    public async Task<IssueRecord> UpdateIssueAsync(string issueId, string? title = null, string? body = null, string? state = null, string[]? labels = null, string? project = null, string? stepId = null)
    {
        var reqObj = new Dictionary<string, object>();
        if (title != null) reqObj["title"] = title;
        if (body != null) reqObj["body"] = body;
        if (state != null) reqObj["state"] = state;

        List<string>? updatedLabels = null;
        if (labels != null) 
        {
            updatedLabels = labels.ToList();
        }
        else if (project != null)
        {
            var existingIssue = await GetIssueAsync(issueId);
            if (existingIssue != null) 
            {
                updatedLabels = existingIssue.Labels;
            }
        }

        if (updatedLabels != null)
        {
            if (project != null)
            {
                updatedLabels.RemoveAll(l => l.StartsWith("project: ", StringComparison.OrdinalIgnoreCase));
                // We keep the project string for GraphQL Sync, but do NOT push it to GitHub labels.
            }
            
            // Strip project labels from REST payload
            var restLabels = updatedLabels.Where(l => !l.StartsWith("project: ", StringComparison.OrdinalIgnoreCase)).ToList();
            reqObj["labels"] = restLabels.ToArray();
        }

        using var req = CreateGitHubRequest(HttpMethod.Patch, $"issues/{Uri.EscapeDataString(issueId)}", reqObj);
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        var record = ghIssue?.ToRecord(_environmentName) ?? new IssueRecord();

        if (!string.IsNullOrWhiteSpace(record.NodeId)) {
            await EnrichIssuesWithProjectFieldsAsync(new List<IssueRecord> { record });
            if (project != null) {
                record.Project = project;
            }
            
            if (stepId != null) {
                record.StepId = stepId;
            }
            await SyncProjectV2Async(record);
        }
        return record;
    }

    public async Task<bool> DeleteIssueAsync(string issueId)
    {
        var record = await GetIssueAsync(issueId);
        if (record == null || string.IsNullOrWhiteSpace(record.NodeId)) return false;

        var mut = @"mutation($issueId: ID!) { deleteIssue(input: {issueId: $issueId}) { clientMutationId } }";
        var res = await SendGraphQLRequestAsync(new { query = mut, variables = new { issueId = record.NodeId } });
        return res.Contains("deleteIssue");
    }

    public async Task<string> AddIssueCommentAsync(string issueId, string body)
    {
        using var req = CreateGitHubRequest(HttpMethod.Post, $"issues/{Uri.EscapeDataString(issueId)}/comments", new { body });
        return await SendGitHubRequestAsync(req);
    }

    private async Task EnsureProjectsCachedAsync()
    {
        if (_projectNodeIds.Any()) return;

        var query = @"
query($owner: String!) {
  organization(login: $owner) {
    projectsV2(first: 20) {
      nodes {
        id
        title
        fields(first: 20) {
          nodes {
            ... on ProjectV2SingleSelectField {
              id
              name
              options { id name }
            }
          }
        }
      }
    }
  }
}";
        try {
            var payload = new { query, variables = new { owner = _config.Owner } };
            var json = await SendGraphQLRequestAsync(payload);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("organization", out var org) && org.ValueKind != JsonValueKind.Null) {
                ParseProjectsV2(org);
            } else {
                var userQuery = query.Replace("organization(login: $owner)", "user(login: $owner)");
                var userJson = await SendGraphQLRequestAsync(new { query = userQuery, variables = new { owner = _config.Owner } });
                var userDoc = JsonDocument.Parse(userJson);
                if (userDoc.RootElement.TryGetProperty("data", out var uData) && uData.TryGetProperty("user", out var usr) && usr.ValueKind != JsonValueKind.Null) {
                    ParseProjectsV2(usr);
                }
            }
        } catch { /* Ignore cache failures */ }
    }

    private void ParseProjectsV2(JsonElement entity)
    {
        if (!entity.TryGetProperty("projectsV2", out var pV2)) return;
        foreach (var pNode in pV2.GetProperty("nodes").EnumerateArray()) {
            var title = pNode.GetProperty("title").GetString();
            var id = pNode.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(id)) continue;

            var statusDict = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

            if (pNode.TryGetProperty("fields", out var fields)) {
                foreach (var fNode in fields.GetProperty("nodes").EnumerateArray()) {
                    if (fNode.TryGetProperty("name", out var fName) && string.Equals(fName.GetString(), "Status", StringComparison.OrdinalIgnoreCase)) {
                        var fieldId = fNode.GetProperty("id").GetString();
                        if (fNode.TryGetProperty("options", out var options) && fieldId != null) {
                            foreach (var opt in options.EnumerateArray()) {
                                var oName = opt.GetProperty("name").GetString();
                                var oId = opt.GetProperty("id").GetString();
                                if (oName != null && oId != null) statusDict[oName] = (fieldId, oId);
                            }
                        }
                    }
                }
            }
            
            // Protect against Duplicate GitHub Titles by scoring native column count layouts statically
            if (_projectStatuses.TryGetValue(title, out var existingDict)) {
                if (statusDict.Count > existingDict.Count) {
                    _projectNodeIds[title] = id;
                    _projectStatuses[title] = statusDict;
                }
            } else {
                _projectNodeIds[title] = id;
                _projectStatuses[title] = statusDict;
            }
        }
    }

    private async Task EnrichIssuesWithProjectFieldsAsync(List<IssueRecord> issues)
    {
        if (!issues.Any() || _config.ProjectTitles == null || !_config.ProjectTitles.Any()) return;
        
        await EnsureProjectsCachedAsync();
        
        var query = @"
query($owner: String!) {
  organization(login: $owner) {
    projectsV2(first: 20) {
      nodes {
        title
        items(first: 100) {
          nodes {
            content { ... on Issue { number } }
            fieldValues(first: 10) {
              nodes {
                ... on ProjectV2ItemFieldSingleSelectValue {
                  name
                  field { ... on ProjectV2FieldCommon { name } }
                }
              }
            }
          }
        }
      }
    }
  }
}";
        try {
            JsonElement? orgNode = null;
            var json = await SendGraphQLRequestAsync(new { query, variables = new { owner = _config.Owner } });
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("organization", out var org) && org.ValueKind != JsonValueKind.Null) {
                orgNode = org;
            } else {
                var userJson = await SendGraphQLRequestAsync(new { query = query.Replace("organization", "user"), variables = new { owner = _config.Owner } });
                var userDoc = JsonDocument.Parse(userJson);
                if (userDoc.RootElement.TryGetProperty("data", out var uData) && uData.TryGetProperty("user", out var usr) && usr.ValueKind != JsonValueKind.Null) {
                    orgNode = usr;
                }
            }

            if (orgNode == null || !orgNode.Value.TryGetProperty("projectsV2", out var pV2)) return;

            var issueMap = issues.ToDictionary(i => i.Id);

            foreach (var pNode in pV2.GetProperty("nodes").EnumerateArray()) {
                var pTitle = pNode.GetProperty("title").GetString();
                if (pTitle == null || !_config.ProjectTitles.Values.Contains(pTitle, StringComparer.OrdinalIgnoreCase)) continue;

                if (!pNode.TryGetProperty("items", out var items)) continue;
                foreach (var item in items.GetProperty("nodes").EnumerateArray()) {
                    if (!item.TryGetProperty("content", out var content) || !content.TryGetProperty("number", out var numberEl)) continue;
                    var numStr = numberEl.GetInt32().ToString();
                    
                    if (issueMap.TryGetValue(numStr, out var issue)) {
                        var logicalKey = _config.ProjectTitles.FirstOrDefault(kv => string.Equals(kv.Value, pTitle, StringComparison.OrdinalIgnoreCase)).Key;
                        if (logicalKey != null) issue.Project = logicalKey;
                        
                        // Parse status mapped to step
                        if (item.TryGetProperty("fieldValues", out var fVals)) {
                            foreach (var fv in fVals.GetProperty("nodes").EnumerateArray()) {
                                if (fv.TryGetProperty("field", out var fNode) && fNode.TryGetProperty("name", out var fName) && string.Equals(fName.GetString(), "Status", StringComparison.OrdinalIgnoreCase)) {
                                    if (fv.TryGetProperty("name", out var optName)) {
                                        var statusStr = optName.GetString();
                                        if (!string.IsNullOrWhiteSpace(statusStr)) {
                                            issue.StepId = statusStr;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        } catch { /* Ignore enrichment failure gracefully */ }
    }

    private async Task SyncProjectV2Async(IssueRecord issue)
    {
        if (_config.ProjectTitles == null || !_config.ProjectTitles.Any()) return;
        
        await EnsureProjectsCachedAsync();
        
        var issueQuery = @"
query($nodeId: ID!) {
  node(id: $nodeId) {
    ... on Issue {
      projectItems(first: 10) {
        nodes {
          id
          project { id title }
        }
      }
    }
  }
}";
        var json = await SendGraphQLRequestAsync(new { query = issueQuery, variables = new { nodeId = issue.NodeId } });
        var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("node", out var node) || !node.TryGetProperty("projectItems", out var pItems)) return;
        
        string? targetItemNodeId = null;
        var targetGithubTitle = string.Empty;
        if (!string.IsNullOrWhiteSpace(issue.Project) && _config.ProjectTitles.TryGetValue(issue.Project, out var mappedTitle)) {
            targetGithubTitle = mappedTitle;
        }

        foreach (var item in pItems.GetProperty("nodes").EnumerateArray()) {
           var itemId = item.GetProperty("id").GetString();
           var pTitle = item.GetProperty("project").GetProperty("title").GetString();
           var pId = item.GetProperty("project").GetProperty("id").GetString();

           if (!string.IsNullOrWhiteSpace(targetGithubTitle) 
               && string.Equals(pTitle, targetGithubTitle, StringComparison.OrdinalIgnoreCase)
               && _projectNodeIds.TryGetValue(targetGithubTitle, out var expectedPId) 
               && pId == expectedPId) {
               targetItemNodeId = itemId;
           } else if (pTitle != null && _config.ProjectTitles.Values.Contains(pTitle, StringComparer.OrdinalIgnoreCase)) {
               Console.ForegroundColor = ConsoleColor.Cyan;
               Console.WriteLine($"[GitHub Connector Info] Detected orphaned ticket on incorrectly mapped '{pTitle}' board. Evicting...");
               Console.ResetColor();
               var delMut = @"mutation($projectId: ID!, $itemId: ID!) { deleteProjectV2Item(input: {projectId: $projectId, itemId: $itemId}) { deletedItemId } }";
               await SendGraphQLRequestAsync(new { query = delMut, variables = new { projectId = pId, itemId = itemId } }, throwGraphQLErrors: false);
           }
        }

        if (targetItemNodeId == null && !string.IsNullOrWhiteSpace(targetGithubTitle) && _projectNodeIds.TryGetValue(targetGithubTitle, out var targetProjectId)) {
            var addMut = @"mutation($projectId: ID!, $contentId: ID!) { addProjectV2ItemById(input: {projectId: $projectId, contentId: $contentId}) { item { id } } }";
            var addJson = await SendGraphQLRequestAsync(new { query = addMut, variables = new { projectId = targetProjectId, contentId = issue.NodeId } }, throwGraphQLErrors: true);
            var addDoc = JsonDocument.Parse(addJson);
            if (addDoc.RootElement.TryGetProperty("data", out var addData) && addData.TryGetProperty("addProjectV2ItemById", out var added) && added.ValueKind != JsonValueKind.Null) {
                if (added.TryGetProperty("item", out var addedItem) && addedItem.ValueKind != JsonValueKind.Null) {
                    targetItemNodeId = addedItem.GetProperty("id").GetString();
                }
            }
        }

        var stepId = issue.StepId;
        if (targetItemNodeId != null && !string.IsNullOrWhiteSpace(targetGithubTitle) && !string.IsNullOrWhiteSpace(stepId)) {
            if (_projectStatuses.TryGetValue(targetGithubTitle, out var statusOptions) && statusOptions.TryGetValue(stepId, out var statusIds)) {
                var updateMut = @"mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $optionId: String!) { updateProjectV2ItemFieldValue(input: {projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: { singleSelectOptionId: $optionId }}) { projectV2Item { id } } }";
                await SendGraphQLRequestAsync(new { query = updateMut, variables = new { projectId = _projectNodeIds[targetGithubTitle], itemId = targetItemNodeId, fieldId = statusIds.fieldId, optionId = statusIds.optionId } }, throwGraphQLErrors: true);
            }
        }
    }

    private class GitHubLabel { public string name { get; set; } = string.Empty; }
    private class GitHubIssue
    {
        public int number { get; set; }
        public string node_id { get; set; } = string.Empty;
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
            
            var projectPrefix = "project: ";
            var projectLabel = labelsList.FirstOrDefault(l => l.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase));
            string projectValue = projectLabel != null ? projectLabel.Substring(projectPrefix.Length).Trim() : string.Empty;

            return new IssueRecord
            {
                Id = number.ToString(),
                NodeId = node_id ?? string.Empty,
                Title = title ?? string.Empty,
                Body = body ?? string.Empty,
                State = state ?? string.Empty,
                Project = projectValue,
                Labels = labelsList
            };
        }
    }
}
