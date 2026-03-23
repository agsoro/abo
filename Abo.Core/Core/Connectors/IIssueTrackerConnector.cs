using Abo.Contracts.Models;

namespace Abo.Core.Connectors;

public interface IIssueTrackerConnector
{
    Task<IEnumerable<IssueRecord>> ListIssuesAsync(string? state = null, string[]? labels = null);
    Task<IssueRecord?> GetIssueAsync(string issueId);
    Task<IssueRecord> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null, string? project = null);
    Task<IssueRecord> UpdateIssueAsync(string issueId, string? title = null, string? body = null, string? state = null, string[]? labels = null, string? project = null);
    Task<string> AddIssueCommentAsync(string issueId, string body);
}
