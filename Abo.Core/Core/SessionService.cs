using System.Collections.Concurrent;
using Abo.Contracts.OpenAI;

namespace Abo.Core;

/// <summary>
/// A simple in-memory session store for conversation history and issue tracking.
/// </summary>
public class SessionService
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _history = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastActivity = new();
    private readonly ConcurrentDictionary<string, (string IssueId, string? Title)> _currentIssue = new();
    
    /// <summary>
    /// Tracks completed sessions with their completion timestamps.
    /// Sessions are automatically removed after the retention period.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _completedSessions = new();
    
    /// <summary>
    /// How long to keep completed sessions in the tracking dictionary before cleanup.
    /// </summary>
    private static readonly TimeSpan CompletedSessionRetention = TimeSpan.FromMinutes(60);
    
    private const int MaxHistoryMessages = 20;

    public List<ChatMessage> GetHistory(string sessionId)
    {
        _lastActivity[sessionId] = DateTime.UtcNow;
        return _history.GetOrAdd(sessionId, _ => new List<ChatMessage>());
    }

    public void AddMessage(string sessionId, ChatMessage message)
    {
        _lastActivity[sessionId] = DateTime.UtcNow;
        var history = GetHistory(sessionId);
        lock (history)
        {
            history.Add(message);

            // Keep history lean
            if (history.Count > MaxHistoryMessages)
            {
                int excess = history.Count - MaxHistoryMessages;
                int removeCount = excess;

                // Advance removeCount to the next 'user' message to ensure we do not break tool chains
                // Anthropic API will throw an error if a tool_result does not have a corresponding tool_calls block
                while (removeCount < history.Count && history[removeCount].Role != "user")
                {
                    removeCount++;
                }

                if (removeCount < history.Count)
                {
                    history.RemoveRange(0, removeCount);
                }
            }
        }
    }

    public void ReplaceHistory(string sessionId, List<ChatMessage> newHistory)
    {
        _lastActivity[sessionId] = DateTime.UtcNow;
        _history[sessionId] = newHistory;
    }

    public void ClearHistory(string sessionId)
    {
        _history.TryRemove(sessionId, out _);
        _lastActivity.TryRemove(sessionId, out _);
        _currentIssue.TryRemove(sessionId, out _);
        _completedSessions.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Sets the current issue context for a session.
    /// Also updates the last activity timestamp to ensure the session is tracked as active.
    /// </summary>
    public void SetCurrentIssue(string sessionId, string? issueId, string? issueTitle = null)
    {
        // Track activity so sessions without chat history are still considered active
        _lastActivity[sessionId] = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(issueId))
        {
            _currentIssue.TryRemove(sessionId, out _);
        }
        else
        {
            _currentIssue[sessionId] = (issueId, issueTitle);
        }
    }

    /// <summary>
    /// Gets the current issue context for a session.
    /// </summary>
    public (string? IssueId, string? Title) GetCurrentIssue(string sessionId)
    {
        if (_currentIssue.TryGetValue(sessionId, out var issue))
        {
            return (issue.IssueId, issue.Title);
        }
        return (null, null);
    }

    /// <summary>
    /// Clears the current issue context for a session.
    /// </summary>
    public void ClearCurrentIssue(string sessionId)
    {
        _currentIssue.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Marks a session as completed. This clears the last activity entry so the session
    /// drops from the active sessions list, and records the completion timestamp for
    /// tracking purposes.
    /// </summary>
    public void MarkSessionCompleted(string sessionId)
    {
        // Remove from active tracking
        _lastActivity.TryRemove(sessionId, out _);
        
        // Record completion timestamp
        _completedSessions[sessionId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if a session was explicitly completed.
    /// </summary>
    public bool IsSessionCompleted(string sessionId)
    {
        // Check if it's in the completed sessions dictionary
        if (_completedSessions.TryGetValue(sessionId, out var completedAt))
        {
            // Clean up if the session has been completed beyond retention period
            if (DateTime.UtcNow - completedAt > CompletedSessionRetention)
            {
                _completedSessions.TryRemove(sessionId, out _);
                return false;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Returns a list of recently completed sessions.
    /// Sessions are included if they were completed within the retention period.
    /// </summary>
    public List<SessionInfo> GetCompletedSessions()
    {
        var cutoff = DateTime.UtcNow - CompletedSessionRetention;
        var result = new List<SessionInfo>();

        foreach (var (sessionId, completedAt) in _completedSessions)
        {
            // Skip expired entries
            if (completedAt < cutoff)
            {
                _completedSessions.TryRemove(sessionId, out _);
                continue;
            }

            // Get message count and other info from history
            int messageCount = 0;
            string lastRole = "—";
            if (_history.TryGetValue(sessionId, out var history))
            {
                messageCount = history.Count;
                lastRole = history.LastOrDefault()?.Role ?? "—";
            }

            // Get current issue context if available
            var (issueId, issueTitle) = GetCurrentIssue(sessionId);

            result.Add(new SessionInfo
            {
                SessionId = sessionId,
                MessageCount = messageCount,
                LastActivity = completedAt,
                LastRole = lastRole,
                CurrentIssueId = issueId,
                CurrentIssueTitle = issueTitle,
                IsCompleted = true
            });
        }

        return result.OrderByDescending(s => s.LastActivity).ToList();
    }

    /// <summary>
    /// Clears a specific completed session from tracking.
    /// Used for cleanup when a completed session is no longer needed.
    /// </summary>
    public void ClearCompletedSession(string sessionId)
    {
        _completedSessions.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Returns a list of currently active sessions with their message counts and last activity timestamps.
    /// Sessions with no activity in the last 30 minutes are considered inactive.
    /// Iterates over _lastActivity to include sessions that have context but no chat history yet
    /// (e.g., delegated specialist sessions).
    /// </summary>
    public List<SessionInfo> GetActiveSessions()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var result = new List<SessionInfo>();

        // Iterate over _lastActivity to include sessions that were set up via SetCurrentIssue
        // but have no chat history yet (e.g., delegated specialist sessions).
        foreach (var sessionId in _lastActivity.Keys)
        {
            if (!_lastActivity.TryGetValue(sessionId, out var lastActive)) continue;
            if (lastActive < cutoff) continue;

            // Get message count from _history if available
            int messageCount = 0;
            string lastRole = "—";
            if (_history.TryGetValue(sessionId, out var history))
            {
                messageCount = history.Count;
                lastRole = history.LastOrDefault()?.Role ?? "—";
            }

            // Get current issue context if available
            var (issueId, issueTitle) = GetCurrentIssue(sessionId);

            result.Add(new SessionInfo
            {
                SessionId = sessionId,
                MessageCount = messageCount,
                LastActivity = lastActive,
                LastRole = lastRole,
                CurrentIssueId = issueId,
                CurrentIssueTitle = issueTitle
            });
        }

        return result.OrderByDescending(s => s.LastActivity).ToList();
    }
}

public class SessionInfo
{
    public string SessionId { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime LastActivity { get; set; }
    public string LastRole { get; set; } = string.Empty;
    
    /// <summary>
    /// The ID of the issue currently being processed by this session, if any.
    /// </summary>
    public string? CurrentIssueId { get; set; }
    
    /// <summary>
    /// The title of the issue currently being processed by this session, if any.
    /// </summary>
    public string? CurrentIssueTitle { get; set; }
    
    /// <summary>
    /// Indicates whether this session has been explicitly completed (via stop or conclude_step).
    /// </summary>
    public bool IsCompleted { get; set; }
}
