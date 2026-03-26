using System.Text.Json;
using Abo.Contracts.Models;

namespace Abo.Core.Connectors;

public class FileSystemIssueTrackerConnector : IIssueTrackerConnector
{
    private readonly string _activeIssuesFile;
    private readonly string? _environmentName;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public FileSystemIssueTrackerConnector(string? environmentName = null)
    {
        _environmentName = environmentName;
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var issuesDir = Path.Combine(dataDir, "Issues");
        _activeIssuesFile = Path.Combine(issuesDir, "active_issues.json");
        if (!Directory.Exists(issuesDir)) Directory.CreateDirectory(issuesDir);
    }

    private async Task<List<IssueRecord>> LoadRecordsAsync()
    {
        if (!File.Exists(_activeIssuesFile)) return new List<IssueRecord>();
        var json = await File.ReadAllTextAsync(_activeIssuesFile);
        try {
            var recs = JsonSerializer.Deserialize<List<IssueRecord>>(json, _jsonOptions) ?? new List<IssueRecord>();
            if (!string.IsNullOrWhiteSpace(_environmentName)) {
                foreach(var r in recs) {
                    r.Labels.RemoveAll(l => l.StartsWith("env: ", StringComparison.OrdinalIgnoreCase));
                    r.Labels.Add($"env: {_environmentName}");
                }
            }
            return recs;
        } catch {
            return new List<IssueRecord>();
        }
    }

    private async Task SaveRecordsAsync(List<IssueRecord> records)
    {
        var json = JsonSerializer.Serialize(records, _jsonOptions);
        await File.WriteAllTextAsync(_activeIssuesFile, json);
    }

    public async Task<IEnumerable<IssueRecord>> ListIssuesAsync(string? state = null, string[]? labels = null)
    {
        var records = await LoadRecordsAsync();
        var query = records.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(state))
        {
            if (state.Equals("all", StringComparison.OrdinalIgnoreCase)) { } // no filter
            else query = query.Where(r => r.State.Equals(state, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            query = query.Where(r => !r.State.Equals("closed", StringComparison.OrdinalIgnoreCase));
        }

        if (labels != null && labels.Any())
        {
            // Simple heuristic to match labels accurately or as substring
            query = query.Where(r => labels.All(l => r.Labels.Any(rl => rl.Contains(l, StringComparison.OrdinalIgnoreCase))));
        }

        return query;
    }

    public async Task<IssueRecord?> GetIssueAsync(string issueId, bool includeDetails = false)
    {
        var records = await LoadRecordsAsync();
        var issue = records.FirstOrDefault(r => r.Id == issueId);
        if (issue != null && !includeDetails)
        {
            issue.Comments.Clear();
        }
        return issue;
    }

    public async Task<IssueRecord> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null, string? project = null, string? stepId = null)
    {
        var records = await LoadRecordsAsync();
        
        // Generate a new ID based on existing numeric IDs or a random one
        int maxId = 0;
        foreach(var r in records) {
            if (int.TryParse(r.Id, out int id) && id > maxId) maxId = id;
        }
        var newId = (maxId > 0 ? maxId + 1 : 1).ToString();
        
        var labels = new List<string>();
        if (!string.IsNullOrWhiteSpace(size)) labels.Add($"size: {size}");
        if (additionalLabels != null) labels.AddRange(additionalLabels);
        if (!string.IsNullOrWhiteSpace(project)) labels.Add($"project: {project}");

        var issue = new IssueRecord
        {
            Id = newId,
            Title = title,
            Body = body,
            State = "open",
            Project = project ?? string.Empty,
            StepId = stepId ?? string.Empty,
            Type = type,
            Labels = labels
        };

        records.Add(issue);
        await SaveRecordsAsync(records);
        return issue;
    }

    public async Task<IssueRecord> UpdateIssueAsync(string issueId, string? title = null, string? body = null, string? state = null, string[]? labels = null, string? project = null, string? stepId = null, string? type = null)
    {
        var records = await LoadRecordsAsync();
        var issue = records.FirstOrDefault(r => r.Id == issueId);
        if (issue == null) throw new Exception($"Issue {issueId} not found.");

        if (title != null) issue.Title = title;
        if (body != null) issue.Body = body;
        if (state != null) issue.State = state;
        if (labels != null) issue.Labels = labels.ToList();
        if (stepId != null) issue.StepId = stepId;
        if (type != null) issue.Type = type;

        if (project != null)
        {
            issue.Labels.RemoveAll(l => l.StartsWith("project: ", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(project))
            {
                issue.Labels.Add($"project: {project}");
            }
            issue.Project = project;
        }

        await SaveRecordsAsync(records);
        return issue;
    }

    public async Task<bool> DeleteIssueAsync(string issueId)
    {
        var records = await LoadRecordsAsync();
        var removed = records.RemoveAll(r => r.Id == issueId);
        if (removed > 0)
        {
            await SaveRecordsAsync(records);
            return true;
        }
        return false;
    }

    public async Task<string> AddIssueCommentAsync(string issueId, string body)
    {
        var records = await LoadRecordsAsync();
        var issue = records.FirstOrDefault(r => r.Id == issueId);
        if (issue == null) throw new Exception($"Issue {issueId} not found.");

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        issue.Comments.Add($"### Comment ({timestamp})\n{body}");

        await SaveRecordsAsync(records);
        return "Comment added.";
    }

    /// <summary>
    /// No-op for the filesystem connector — native sub-issue linking is a GitHub concept.
    /// Always returns true to allow the caller to continue with label-based tracking.
    /// </summary>
    public Task<bool> AddSubIssueAsync(string parentIssueNodeId, string childIssueNodeId)
    {
        return Task.FromResult(true);
    }
}
