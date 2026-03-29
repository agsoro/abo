namespace Abo.Core.Models;

/// <summary>
/// Tracks metrics for an agent session to determine when specialist consultation should be suggested.
/// Part of the ConsultSpecialistTool implementation (Issue #407).
/// 
/// This class aggregates usage statistics including message count, cost, and loop detection
/// to provide actionable insights for the ConsultationTrigger.
/// </summary>
public class AgentMetrics
{
    /// <summary>
    /// The session identifier.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the issue being processed, if any.
    /// </summary>
    public string? IssueId { get; set; }

    /// <summary>
    /// The name of the agent running this session.
    /// </summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>
    /// The LLM model being used.
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Current message count in the conversation.
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Accumulated cost in dollars.
    /// </summary>
    public double AccumulatedCost { get; set; }

    /// <summary>
    /// Current loop iteration number.
    /// </summary>
    public int CurrentLoop { get; set; }

    /// <summary>
    /// Percentage of messages that appear to be in a loop (0-100).
    /// Calculated by comparing recent responses for similarity.
    /// </summary>
    public double LoopPercentage { get; set; }

    /// <summary>
    /// When this session started.
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the last activity occurred.
    /// </summary>
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Token usage statistics.
    /// </summary>
    public TokenUsage TokenUsage { get; set; } = new();

    /// <summary>
    /// Number of tool calls made.
    /// </summary>
    public int ToolCallCount { get; set; }

    /// <summary>
    /// Number of errors encountered.
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// Total number of turns taken (message exchanges).
    /// </summary>
    public int TurnCount { get; set; }

    /// <summary>
    /// Whether summarization has been triggered.
    /// </summary>
    public bool SummarizationTriggered { get; set; }

    /// <summary>
    /// Number of summarizations performed.
    /// </summary>
    public int SummarizationCount { get; set; }

    /// <summary>
    /// Track recent responses for loop detection.
    /// </summary>
    private readonly Queue<string> _recentResponses = new();
    private const int MaxRecentResponses = 5;

    /// <summary>
    /// Records a response for loop detection.
    /// </summary>
    /// <param name="responseContent">The content of the response.</param>
    public void RecordResponse(string responseContent)
    {
        LastActivity = DateTime.UtcNow;
        TurnCount++;
        CurrentLoop++;
        
        // Normalize and add to recent responses queue
        var normalized = NormalizeForComparison(responseContent);
        
        _recentResponses.Enqueue(normalized);
        while (_recentResponses.Count > MaxRecentResponses)
        {
            _recentResponses.Dequeue();
        }

        // Calculate loop percentage
        UpdateLoopPercentage();
    }

    /// <summary>
    /// Records an API call for cost tracking.
    /// </summary>
    /// <param name="inputTokens">Input tokens used.</param>
    /// <param name="outputTokens">Output tokens used.</param>
    /// <param name="cost">Cost of the call.</param>
    public void RecordApiCall(int inputTokens, int outputTokens, double cost)
    {
        TokenUsage.TotalInputTokens += inputTokens;
        TokenUsage.TotalOutputTokens += outputTokens;
        TokenUsage.TotalTokens += inputTokens + outputTokens;
        TokenUsage.TotalCost += cost;
        AccumulatedCost += cost;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a message for message count tracking.
    /// </summary>
    public void RecordMessage()
    {
        MessageCount++;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a tool call.
    /// </summary>
    public void RecordToolCall()
    {
        ToolCallCount++;
    }

    /// <summary>
    /// Records an error.
    /// </summary>
    public void RecordError()
    {
        ErrorCount++;
    }

    /// <summary>
    /// Records that summarization was triggered.
    /// </summary>
    public void RecordSummarization()
    {
        SummarizationTriggered = true;
        SummarizationCount++;
    }

    /// <summary>
    /// Updates the loop percentage based on recent responses.
    /// </summary>
    private void UpdateLoopPercentage()
    {
        if (_recentResponses.Count < 2)
        {
            LoopPercentage = 0;
            return;
        }

        var responses = _recentResponses.ToArray();
        int identicalPairs = 0;
        int totalPairs = responses.Length - 1;

        for (int i = 0; i < totalPairs; i++)
        {
            if (responses[i] == responses[i + 1])
            {
                identicalPairs++;
            }
        }

        LoopPercentage = totalPairs > 0 ? (identicalPairs / (double)totalPairs) * 100 : 0;
    }

    /// <summary>
    /// Normalizes content for loop comparison.
    /// Removes whitespace, converts to lowercase, and truncates.
    /// </summary>
    private static string NormalizeForComparison(string content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        // Remove excessive whitespace, convert to lowercase, truncate
        var normalized = content
            .ToLowerInvariant()
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Replace("  ", " ")
            .Trim();

        // Take first 200 chars for comparison
        if (normalized.Length > 200)
            normalized = normalized[..200];

        return normalized;
    }

    /// <summary>
    /// Gets a summary of current metrics.
    /// </summary>
    public string GetSummary()
    {
        return $"Session: {SessionId}, Messages: {MessageCount}, Cost: ${AccumulatedCost:F4}, Loop%: {LoopPercentage:F1}%, Turn: {CurrentLoop}";
    }

    /// <summary>
    /// Creates a copy of these metrics for comparison.
    /// </summary>
    public AgentMetrics Clone()
    {
        return new AgentMetrics
        {
            SessionId = SessionId,
            IssueId = IssueId,
            AgentName = AgentName,
            ModelName = ModelName,
            MessageCount = MessageCount,
            AccumulatedCost = AccumulatedCost,
            CurrentLoop = CurrentLoop,
            LoopPercentage = LoopPercentage,
            StartTime = StartTime,
            LastActivity = LastActivity,
            TokenUsage = new TokenUsage
            {
                TotalInputTokens = TokenUsage.TotalInputTokens,
                TotalOutputTokens = TokenUsage.TotalOutputTokens,
                TotalTokens = TokenUsage.TotalTokens,
                TotalCost = TokenUsage.TotalCost
            },
            ToolCallCount = ToolCallCount,
            ErrorCount = ErrorCount,
            TurnCount = TurnCount,
            SummarizationTriggered = SummarizationTriggered,
            SummarizationCount = SummarizationCount
        };
    }
}

/// <summary>
/// Token usage statistics for tracking API consumption.
/// </summary>
public class TokenUsage
{
    /// <summary>
    /// Total input tokens used.
    /// </summary>
    public int TotalInputTokens { get; set; }

    /// <summary>
    /// Total output tokens used.
    /// </summary>
    public int TotalOutputTokens { get; set; }

    /// <summary>
    /// Total tokens used (input + output).
    /// </summary>
    public int TotalTokens { get; set; }

    /// <summary>
    /// Total cost in dollars.
    /// </summary>
    public double TotalCost { get; set; }

    /// <summary>
    /// Average cost per token.
    /// </summary>
    public double CostPerToken => TotalTokens > 0 ? TotalCost / TotalTokens : 0;

    /// <summary>
    /// Average input cost per million tokens.
    /// </summary>
    public double InputCostPerMillion => TotalInputTokens > 0 ? (TotalCost / 2) / (TotalInputTokens / 1_000_000.0) : 0;
}
