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

    // Stores repository-level built-in issue types: (typeName -> typeNodeId)
    private static readonly Dictionary<string, string> _repoIssueTypes = new(StringComparer.OrdinalIgnoreCase);
    private static bool _repoIssueTypesCached = false;

    // Enrichment result cache: key = issue number (string), value = (Project, Status, Type, CachedAt)
    private static readonly Dictionary<string, (string Project, string Status, string Type, DateTime CachedAt)> _enrichmentCache
        = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan _enrichmentCacheTtl = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// Executes a GraphQL query that targets either an organization or a user (org → user fallback).
    /// The query must use <c>organization(login: $owner)</c>; the fallback replaces it with <c>user(login: $owner)</c>.
    /// Returns the resolved entity element (organization or user node), or null if neither resolves.
    /// </summary>
    private async Task<JsonElement?> QueryOrgOrUserAsync(string query, object variables)
    {
        try
        {
            var json = await SendGraphQLRequestAsync(new { query, variables });
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("organization", out var org) && org.ValueKind != JsonValueKind.Null)
                    return org.Clone();
                if (data.TryGetProperty("user", out var userFallback) && userFallback.ValueKind != JsonValueKind.Null)
                    return userFallback.Clone();
            }
        }
        catch { /* fall through to user query */ }

        try
        {
            var userQuery = query.Replace("organization(login: $owner)", "user(login: $owner)");
            var userJson = await SendGraphQLRequestAsync(new { query = userQuery, variables });
            using var userDoc = JsonDocument.Parse(userJson);
            if (userDoc.RootElement.TryGetProperty("data", out var uData))
            {
                if (uData.TryGetProperty("user", out var usr) && usr.ValueKind != JsonValueKind.Null)
                    return usr.Clone();
                if (uData.TryGetProperty("organization", out var uOrg) && uOrg.ValueKind != JsonValueKind.Null)
                    return uOrg.Clone();
            }
        }
        catch { /* ignore fallback failure */ }

        return null;
    }

    /// <summary>
    /// Queries all configured project titles via GraphQL (Projects V2) and returns all issue items
    /// mapped to <see cref="IssueRecord"/>, keyed by issue number (deduplicated).
    /// Pre-warms <c>_enrichmentCache</c> as a side effect to avoid redundant enrichment calls.
    /// Emits a yellow console warning when a project has more than 100 items (pagination not yet implemented).
    /// </summary>
    private async Task<List<IssueRecord>> FetchProjectItemsAsync()
    {
        await EnsureProjectsCachedAsync();

        var query = @"
query($owner: String!) {
  organization(login: $owner) {
    projectsV2(first: 20) {
      nodes {
        title
        items(first: 100) {
          pageInfo { hasNextPage }
          nodes {
            content {
              ... on Issue {
                number
                title
                body
                state
                id
                issueType { name }
                labels(first: 20) {
                  nodes { name }
                }
              }
            }
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

        var results = new Dictionary<string, IssueRecord>(StringComparer.OrdinalIgnoreCase);

        var orgNode = await QueryOrgOrUserAsync(query, new { owner = _config.Owner });
        if (orgNode == null || !orgNode.Value.TryGetProperty("projectsV2", out var pV2))
            return new List<IssueRecord>();

        foreach (var pNode in pV2.GetProperty("nodes").EnumerateArray())
        {
            var pTitle = pNode.GetProperty("title").GetString();

            // Only process configured projects
            if (pTitle == null || !_config.ProjectTitles.Values.Contains(pTitle, StringComparer.OrdinalIgnoreCase))
                continue;

            var logicalKey = _config.ProjectTitles
                .FirstOrDefault(kv => string.Equals(kv.Value, pTitle, StringComparison.OrdinalIgnoreCase)).Key;

            if (!pNode.TryGetProperty("items", out var items)) continue;

            // Warn on pagination truncation
            if (items.TryGetProperty("pageInfo", out var pi) &&
                pi.TryGetProperty("hasNextPage", out var hn) && hn.GetBoolean())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[GitHub Connector Warning] Project '{pTitle}' has >100 items. Results may be truncated. Pagination not yet implemented.");
                Console.ResetColor();
            }

            foreach (var item in items.GetProperty("nodes").EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
                    continue;
                if (!content.TryGetProperty("number", out var numEl))
                    continue;

                var numStr = numEl.GetInt32().ToString();
                var title = content.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                var body = content.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
                var state = content.TryGetProperty("state", out var stateEl)
                    ? (stateEl.GetString() ?? "OPEN").ToLowerInvariant()
                    : "open";
                var nodeId = content.TryGetProperty("id", out var nidEl) ? nidEl.GetString() ?? "" : "";

                var labelsList = new List<string>();
                if (content.TryGetProperty("labels", out var labelsEl) &&
                    labelsEl.TryGetProperty("nodes", out var labelNodes))
                {
                    foreach (var ln in labelNodes.EnumerateArray())
                    {
                        if (ln.TryGetProperty("name", out var lnName))
                        {
                            var lv = lnName.GetString();
                            if (!string.IsNullOrWhiteSpace(lv)) labelsList.Add(lv);
                        }
                    }
                }

                // Inject environment label (mirrors GitHubIssue.ToRecord logic)
                if (!string.IsNullOrWhiteSpace(_environmentName))
                {
                    labelsList.RemoveAll(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase));
                    labelsList.Add($"env: {_environmentName}");
                }

                // Parse Status and Type from field values
                var status = "";
                var typeStr = "";
                
                if (content.TryGetProperty("issueType", out var typeEl) && typeEl.ValueKind == JsonValueKind.Object)
                {
                    if (typeEl.TryGetProperty("name", out var typeNameEl)) {
                        var tv = typeNameEl.GetString();
                        if (!string.IsNullOrWhiteSpace(tv)) typeStr = tv;
                    }
                }

                if (item.TryGetProperty("fieldValues", out var fVals))
                {
                    foreach (var fv in fVals.GetProperty("nodes").EnumerateArray())
                    {
                        if (fv.TryGetProperty("field", out var fNode) &&
                            fNode.TryGetProperty("name", out var fName))
                        {
                            if (string.Equals(fName.GetString(), "Status", StringComparison.OrdinalIgnoreCase))
                            {
                                if (fv.TryGetProperty("name", out var optName))
                                {
                                    status = optName.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
                var record = new IssueRecord
                {
                    Id = numStr,
                    NodeId = nodeId,
                    Title = title,
                    Body = body,
                    State = state,
                    Project = logicalKey ?? "",
                    Status = status,
                    Type = typeStr,
                    Labels = labelsList
                };

                // Deduplicate by issue number (last-write wins for cross-project issues)
                results[numStr] = record;

                // Pre-warm enrichment cache to avoid redundant GraphQL calls in the same session
                _enrichmentCache[numStr] = (record.Project, record.Status, record.Type, DateTime.UtcNow);
            }
        }

        return results.Values.ToList();
    }

    public async Task<IEnumerable<IssueRecord>> ListIssuesAsync(string? state = null, string[]? labels = null)
    {
        // When ProjectTitles are configured, use GraphQL project items as the primary data source
        if (_config.ProjectTitles != null && _config.ProjectTitles.Any())
        {
            try
            {
                var allItems = await FetchProjectItemsAsync();

                // Apply state filter locally (GraphQL returns state as OPEN/CLOSED; already normalized to lowercase)
                if (!string.IsNullOrWhiteSpace(state))
                    allItems = allItems
                        .Where(i => string.Equals(i.State, state, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                // Apply label filter locally (all requested labels must be present)
                if (labels != null && labels.Any())
                    allItems = allItems
                        .Where(i => labels.All(l => i.Labels.Contains(l, StringComparer.OrdinalIgnoreCase)))
                        .ToList();

                return allItems;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[GitHub Connector Warning] GraphQL project-scoped list failed: {ex.Message}. Falling back to REST.");
                Console.ResetColor();
                // Fall through to REST fallback below
            }
        }

        // Fallback: REST-based listing (no ProjectTitles configured, or GraphQL failed)
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

    public async Task<IssueRecord?> GetIssueAsync(string issueId, bool includeDetails = false)
    {
        using var req = CreateGitHubRequest(HttpMethod.Get, $"issues/{Uri.EscapeDataString(issueId)}");
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        var record = ghIssue?.ToRecord(_environmentName);
        if (record != null) 
        {
            await EnrichIssuesWithProjectFieldsAsync(new List<IssueRecord> { record });
            
            if (includeDetails)
            {
                using var commentsReq = CreateGitHubRequest(HttpMethod.Get, $"issues/{Uri.EscapeDataString(issueId)}/comments");
                try
                {
                    var commentsJson = await SendGitHubRequestAsync(commentsReq);
                    using var commentsDoc = JsonDocument.Parse(commentsJson);
                    foreach(var c in commentsDoc.RootElement.EnumerateArray())
                    {
                        if (c.TryGetProperty("body", out var bodyEl))
                        {
                            var bodyText = bodyEl.GetString();
                            if (!string.IsNullOrWhiteSpace(bodyText))
                            {
                                record.Comments.Add(bodyText);
                            }
                        }
                    }
                }
                catch { /* Ignore comment fetch failure */ }
            }
        }
        return record;
    }

    public async Task<IssueRecord> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null, string? project = null, string? status = null)
    {
        var labelsList = new List<string>();
        // Note: type is NOT added as a label — it is written to the Projects V2 Type field via SyncProjectV2Async
        if (!string.IsNullOrWhiteSpace(size)) labelsList.Add($"size: {size}");
        if (additionalLabels != null) labelsList.AddRange(additionalLabels);
        if (!string.IsNullOrWhiteSpace(project)) labelsList.Add($"project: {project}");

        var restLabels = labelsList.Where(l => !l.StartsWith("project: ", StringComparison.OrdinalIgnoreCase)).ToList();

        var cleanedTitle = title;
        if (cleanedTitle.StartsWith("[abo] ", StringComparison.OrdinalIgnoreCase)) {
            cleanedTitle = cleanedTitle.Substring(6).TrimStart();
        }

        var taggedTitle = $"[abo] {cleanedTitle}";
        var reqObj = new
        {
            title = taggedTitle,
            body,
            labels = restLabels.Any() ? restLabels : null
        };

        using var req = CreateGitHubRequest(HttpMethod.Post, "issues", reqObj);
        var json = await SendGitHubRequestAsync(req);
        var ghIssue = JsonSerializer.Deserialize<GitHubIssue>(json, _jsonOptions);
        var record = ghIssue?.ToRecord(_environmentName) ?? new IssueRecord();
        
        if (!string.IsNullOrWhiteSpace(record.NodeId)) {
            record.Project = project ?? string.Empty;
            record.Status = status ?? string.Empty;
            record.Type = type;
            await SyncProjectV2Async(record);
            // Invalidate cache entry so the next fetch reflects the new project state
            _enrichmentCache.Remove(record.Id);
        }
        return record;
    }

    public async Task<IssueRecord> UpdateIssueAsync(string issueId, string? title = null, string? body = null, string? state = null, string[]? labels = null, string? project = null, string? status = null, string? type = null, string? size = null)
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
        else if (project != null || size != null || type != null || status != null)
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

            if (size != null)
            {
                updatedLabels.RemoveAll(l => l.StartsWith("size: ", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(size))
                {
                    updatedLabels.Add($"size: {size}");
                }
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
            
            if (status != null) {
                record.Status = status;
            }

            if (type != null) {
                record.Type = type;
            }

            await SyncProjectV2Async(record);
            // Proactively warm the cache so the next immediate fetch is consistent, avoiding GitHub's indexing delays
            _enrichmentCache[issueId] = (record.Project, record.Status, record.Type, DateTime.UtcNow);
        }
        return record;
    }

    public async Task<bool> DeleteIssueAsync(string issueId)
    {
        var record = await GetIssueAsync(issueId);
        if (record == null || string.IsNullOrWhiteSpace(record.NodeId)) return false;

        var mut = @"mutation($issueId: ID!) { deleteIssue(input: {issueId: $issueId}) { clientMutationId } }";
        var res = await SendGraphQLRequestAsync(new { query = mut, variables = new { issueId = record.NodeId } });
        if (res.Contains("deleteIssue"))
        {
            _enrichmentCache.Remove(issueId);
            return true;
        }
        return false;
    }

    public async Task<string> AddIssueCommentAsync(string issueId, string body)
    {
        var taggedBody = $"[abo]\n{body}";
        using var req = CreateGitHubRequest(HttpMethod.Post, $"issues/{Uri.EscapeDataString(issueId)}/comments", new { body = taggedBody });
        return await SendGitHubRequestAsync(req);
    }

    /// <summary>
    /// Links a child issue as a native GitHub sub-issue of the parent using the GraphQL <c>addSubIssue</c> mutation.
    /// Returns true on success, false on failure (graceful degradation — label-based tracking is the fallback).
    /// </summary>
    public async Task<bool> AddSubIssueAsync(string parentIssueNodeId, string childIssueNodeId)
    {
        try
        {
            var mutation = @"
mutation AddSubIssue($parentIssueId: ID!, $childIssueId: ID!) {
  addSubIssue(input: { issueId: $parentIssueId, subIssueId: $childIssueId }) {
    issue { id number }
    subIssue { id number }
  }
}";
            var result = await SendGraphQLRequestAsync(
                new { query = mutation, variables = new { parentIssueId = parentIssueNodeId, childIssueId = childIssueNodeId } },
                throwGraphQLErrors: false);

            // Check if the mutation returned valid data (not just errors)
            using var doc = JsonDocument.Parse(result);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("addSubIssue", out var addSubIssue) &&
                addSubIssue.ValueKind != JsonValueKind.Null)
            {
                return true;
            }

            // Log warning if there were GraphQL errors but don't throw
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var firstMsg = errors.GetArrayLength() > 0
                    ? errors[0].GetProperty("message").GetString()
                    : "unknown error";
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[GitHub Connector Warning] addSubIssue GraphQL returned errors: {firstMsg}. Falling back to label-based tracking.");
                Console.ResetColor();
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[GitHub Connector Warning] AddSubIssueAsync failed: {ex.Message}. Falling back to label-based tracking.");
            Console.ResetColor();
            return false;
        }
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
            var entity = await QueryOrgOrUserAsync(query, new { owner = _config.Owner });
            if (entity.HasValue)
            {
                ParseProjectsV2(entity.Value);
            }
        } catch { /* Ignore cache failures */ }

        await EnsureRepoIssueTypesCachedAsync();
    }

    private async Task EnsureRepoIssueTypesCachedAsync()
    {
        if (_repoIssueTypesCached) return;

        var query = @"
query($owner: String!, $repo: String!) {
  repository(owner: $owner, name: $repo) {
    owner { id }
    issueTypes(first: 20) {
      nodes {
        id
        name
      }
    }
  }
}";
        try {
            var json = await SendGraphQLRequestAsync(new { query, variables = new { owner = _config.Owner, repo = _config.Repository } });
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && 
                data.TryGetProperty("repository", out var repoNode))
            {
                var ownerId = repoNode.TryGetProperty("owner", out var ownerEl) ? ownerEl.GetProperty("id").GetString() : null;
                
                if (repoNode.TryGetProperty("issueTypes", out var issueTypesNode) &&
                    issueTypesNode.TryGetProperty("nodes", out var nodes))
                {
                    foreach (var node in nodes.EnumerateArray())
                    {
                        var id = node.GetProperty("id").GetString();
                        var name = node.GetProperty("name").GetString();
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        {
                            _repoIssueTypes[name] = id;
                        }
                    }
                }

                // Check and auto-provision missing allowed types mapped from Abo.Contracts
                if (!string.IsNullOrEmpty(ownerId))
                {
                    bool addedAny = false;
                    foreach (var requiredType in Abo.Contracts.Models.IssueType.AllowedValues)
                    {
                        var exists = _repoIssueTypes.Keys.Any(k => string.Equals(k, requiredType, StringComparison.OrdinalIgnoreCase));
                        if (!exists)
                        {
                            // Natively capitalize types ("bug" -> "Bug")
                            var formattedName = char.ToUpperInvariant(requiredType[0]) + requiredType.Substring(1);
                            var mut = @"
mutation($ownerId: ID!, $name: String!) {
  createIssueType(input: { ownerId: $ownerId, name: $name, isEnabled: true }) {
    issueType { id name }
  }
}";
                            try
                            {
                                var mutJson = await SendGraphQLRequestAsync(new { 
                                    query = mut, 
                                    variables = new { ownerId, name = formattedName } 
                                });
                                var mutDoc = JsonDocument.Parse(mutJson);
                                if (mutDoc.RootElement.TryGetProperty("errors", out var errs))
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"[GitHub Connector Error] GraphQL errors provisioning '{formattedName}': {errs.GetRawText()}");
                                    Console.ResetColor();
                                }
                                
                                if (mutDoc.RootElement.TryGetProperty("data", out var mData) &&
                                    mData.ValueKind == JsonValueKind.Object &&
                                    mData.TryGetProperty("createIssueType", out var cIt) &&
                                    cIt.ValueKind == JsonValueKind.Object &&
                                    cIt.TryGetProperty("issueType", out var itNode) &&
                                    itNode.ValueKind == JsonValueKind.Object)
                                {
                                    var newId = itNode.GetProperty("id").GetString();
                                    var newName = itNode.GetProperty("name").GetString();
                                    if (!string.IsNullOrEmpty(newId) && !string.IsNullOrEmpty(newName))
                                    {
                                        _repoIssueTypes[newName] = newId;
                                        addedAny = true;
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($"[GitHub Connector] Auto-provisioned native Issue Type: '{newName}'");
                                        Console.ResetColor();
                                    }
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"[GitHub Connector Error] Unexpected response for '{formattedName}': {mutJson}");
                                    Console.ResetColor();
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[GitHub Connector Error] Failed to auto-provision native Issue Type '{formattedName}': {ex.Message}");
                                Console.ResetColor();
                            }
                        }
                    }
                    if (addedAny)
                    {
                        Console.WriteLine("[GitHub Connector] Repo Issue Types auto-provisioning complete.");
                    }
                }
            }
            _repoIssueTypesCached = true;
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
                    if (!fNode.TryGetProperty("name", out var fName)) continue;

                    if (string.Equals(fName.GetString(), "Status", StringComparison.OrdinalIgnoreCase)) {
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

        // Short-circuit: if all issues have a fresh cache entry, apply cached values without any API call
        var now = DateTime.UtcNow;
        var allCached = issues.All(i =>
            _enrichmentCache.TryGetValue(i.Id, out var entry) &&
            (now - entry.CachedAt) < _enrichmentCacheTtl);

        if (allCached)
        {
            foreach (var issue in issues)
            {
                var cached = _enrichmentCache[issue.Id];
                if (string.IsNullOrEmpty(issue.Project)) issue.Project = cached.Project;
                if (string.IsNullOrEmpty(issue.Status))  issue.Status  = cached.Status;
                if (string.IsNullOrEmpty(issue.Type))    issue.Type    = cached.Type;
            }
            return;
        }
        
        await EnsureProjectsCachedAsync();
        
        var query = @"
query($owner: String!) {
  organization(login: $owner) {
    projectsV2(first: 20) {
      nodes {
        title
        items(first: 100) {
          nodes {
            content { ... on Issue { number issueType { name } } }
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
            var orgNode = await QueryOrgOrUserAsync(query, new { owner = _config.Owner });

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
                        
                        if (content.TryGetProperty("issueType", out var typeEl) && typeEl.ValueKind == JsonValueKind.Object) {
                            if (typeEl.TryGetProperty("name", out var typeNameEl)) {
                                var typeStr = typeNameEl.GetString();
                                if (!string.IsNullOrWhiteSpace(typeStr)) 
                                {
                                    var mappedType = Abo.Contracts.Models.IssueType.AllowedValues.FirstOrDefault(t => string.Equals(t, typeStr, StringComparison.OrdinalIgnoreCase));
                                    issue.Type = mappedType ?? typeStr.ToLowerInvariant();
                                }
                            }
                        }
                        
                        // Parse Status from field values
                        if (item.TryGetProperty("fieldValues", out var fVals)) {
                            foreach (var fv in fVals.GetProperty("nodes").EnumerateArray()) {
                                if (fv.TryGetProperty("field", out var fNode) &&
                                    fNode.TryGetProperty("name", out var fName))
                                {
                                    if (string.Equals(fName.GetString(), "Status", StringComparison.OrdinalIgnoreCase)) {
                                        if (fv.TryGetProperty("name", out var optName)) {
                                            var statusStr = optName.GetString();
                                            if (!string.IsNullOrWhiteSpace(statusStr)) {
                                                issue.Status = statusStr;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Write back all enriched issues to the cache
            foreach (var rec in issues)
            {
                _enrichmentCache[rec.Id] = (rec.Project, rec.Status, rec.Type, DateTime.UtcNow);
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

        // Sync Status field
        var status = issue.Status;
        if (targetItemNodeId != null && !string.IsNullOrWhiteSpace(targetGithubTitle) && !string.IsNullOrWhiteSpace(status)) {
            if (_projectStatuses.TryGetValue(targetGithubTitle, out var statusOptions) && statusOptions.TryGetValue(status, out var statusIds)) {
                var updateMut = @"mutation($projectId: ID!, $itemId: ID!, $fieldId: ID!, $optionId: String!) { updateProjectV2ItemFieldValue(input: {projectId: $projectId, itemId: $itemId, fieldId: $fieldId, value: { singleSelectOptionId: $optionId }}) { projectV2Item { id } } }";
                await SendGraphQLRequestAsync(new { query = updateMut, variables = new { projectId = _projectNodeIds[targetGithubTitle], itemId = targetItemNodeId, fieldId = statusIds.fieldId, optionId = statusIds.optionId } }, throwGraphQLErrors: true);
            }
        }

        // Sync Type field via repository built-in Issue Types
        if (!string.IsNullOrWhiteSpace(issue.Type))
        {
            await EnsureRepoIssueTypesCachedAsync();
            if (_repoIssueTypes.TryGetValue(issue.Type, out var issueTypeId))
            {
                var updateTypeMut = @"mutation($issueId: ID!, $typeId: ID!) {
                    updateIssue(input: { id: $issueId, issueTypeId: $typeId }) {
                        issue { id }
                    }
                }";
                var res = await SendGraphQLRequestAsync(new {
                    query = updateTypeMut,
                    variables = new {
                        issueId = issue.NodeId,
                        typeId = issueTypeId
                    }
                }, throwGraphQLErrors: false);
                Console.WriteLine($"[UpdateIssue GraphQL] {res}");
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

            var rawTitle = title ?? string.Empty;
            string extractedType = string.Empty;

            var titleWithoutAbo = rawTitle.StartsWith("[abo] ", StringComparison.OrdinalIgnoreCase) 
                ? rawTitle.Substring(6).TrimStart() 
                : rawTitle;

            var colonIdx = titleWithoutAbo.IndexOf(':');
            if (colonIdx > 0)
            {
                var potentialType = titleWithoutAbo.Substring(0, colonIdx).Trim().ToLowerInvariant();
                if (IssueType.IsValid(potentialType))
                {
                    extractedType = potentialType;
                }
            }

            return new IssueRecord
            {
                Id = number.ToString(),
                NodeId = node_id ?? string.Empty,
                Title = rawTitle,
                Body = body ?? string.Empty,
                State = state ?? string.Empty,
                Project = projectValue,
                Type = extractedType,
                Labels = labelsList
            };
        }
    }
}
