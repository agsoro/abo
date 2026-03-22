namespace Abo.Core.Connectors;

public interface IIssueTrackerConnector
{
    Task<string> ListIssuesAsync(string? state = null, string[]? labels = null);
    Task<string> GetIssueAsync(string issueId);
    Task<string> CreateIssueAsync(string title, string body, string type, string size, string[]? additionalLabels = null);
    Task<string> AddIssueCommentAsync(string issueId, string body);
}
