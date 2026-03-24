using Abo.Contracts.Models;

namespace Abo.Core.Connectors;

public interface IIssueTrackerConnector
{
    Task<IEnumerable<IssueRecord>> ListIssuesAsync(string? state = null, string[]? labels = null);
    Task<IssueRecord?> GetIssueAsync(string issueId, bool includeDetails = false);
    Task<IssueRecord> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null, string? project = null, string? stepId = null);
    Task<IssueRecord> UpdateIssueAsync(string issueId, string? title = null, string? body = null, string? state = null, string[]? labels = null, string? project = null, string? stepId = null);
    Task<bool> DeleteIssueAsync(string issueId);
    Task<string> AddIssueCommentAsync(string issueId, string body);

    /// <summary>
    /// Links a child issue as a sub-issue of the parent using the native issue tracker API.
    /// Returns true on success, false if the operation is not supported or fails gracefully.
    /// </summary>
    Task<bool> AddSubIssueAsync(string parentIssueNodeId, string childIssueNodeId);
}
