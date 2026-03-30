namespace Abo.Core.Models;

/// <summary>
/// Represents a consultation request between an agent and a specialist.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class ConsultationRequest
{
    /// <summary>
    /// Unique identifier for this consultation session.
    /// </summary>
    public string ConsultationId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// The name of the calling agent (e.g., "ManagerAgent").
    /// </summary>
    public string CallerAgentName { get; set; } = string.Empty;

    /// <summary>
    /// The domain or specialty area requested (optional).
    /// Examples: "architecture", "security", "performance", "database"
    /// </summary>
    public string? SpecialistDomain { get; set; }

    /// <summary>
    /// Detailed description of the task requiring specialist input.
    /// </summary>
    public string TaskDescription { get; set; } = string.Empty;

    /// <summary>
    /// Summary of the broader context (issue details, recent work, constraints).
    /// </summary>
    public string ContextSummary { get; set; } = string.Empty;

    /// <summary>
    /// The session ID of the calling agent (for tracking purposes).
    /// </summary>
    public string? ParentSessionId { get; set; }

    /// <summary>
    /// Optional issue ID for tracking.
    /// </summary>
    public string? IssueId { get; set; }

    /// <summary>
    /// Timestamp when the consultation was requested.
    /// </summary>
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Maximum turns allowed for this consultation (default: 5).
    /// </summary>
    public int MaxTurns { get; set; } = 5;

    /// <summary>
    /// Optional timeout configuration for this consultation.
    /// </summary>
    public ConsultationTimeoutConfig? TimeoutConfig { get; set; }
}

/// <summary>
/// Represents a single message in a specialist consultation exchange.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class ConsultationMessage
{
    /// <summary>
    /// Unique identifier for this consultation session.
    /// </summary>
    public string ConsultationId { get; set; } = string.Empty;

    /// <summary>
    /// Current turn number (starts at 1).
    /// </summary>
    public int TurnNumber { get; set; } = 1;

    /// <summary>
    /// Who sent this message: "caller" or "specialist".
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The content of the message (supports markdown, code blocks, structured recommendations).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the message.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional metadata for structured responses.
    /// </summary>
    public ConsultationMessageMetadata? Metadata { get; set; }
}

/// <summary>
/// Metadata for consultation messages, supporting structured responses.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class ConsultationMessageMetadata
{
    /// <summary>
    /// Message type: "task", "response", "clarification", "recommendation".
    /// </summary>
    public string MessageType { get; set; } = "response";

    /// <summary>
    /// For structured recommendations with priority and category.
    /// </summary>
    public List<StructuredRecommendation>? Recommendations { get; set; }

    /// <summary>
    /// For code blocks with language hints.
    /// </summary>
    public string? CodeLanguage { get; set; }

    /// <summary>
    /// Indicates if this message contains follow-up questions.
    /// </summary>
    public bool ContainsFollowUpQuestions { get; set; }
}

/// <summary>
/// A structured recommendation from the specialist agent.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class StructuredRecommendation
{
    /// <summary>
    /// Priority level: "high", "medium", "low".
    /// </summary>
    public string Priority { get; set; } = "medium";

    /// <summary>
    /// Category: "implementation", "security", "performance", "architecture", etc.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Description of the recommendation.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional list of specific action items.
    /// </summary>
    public List<string>? ActionItems { get; set; }
}

/// <summary>
/// Configuration for consultation timeouts.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class ConsultationTimeoutConfig
{
    /// <summary>
    /// Timeout per turn in seconds (default: 60).
    /// </summary>
    public int TurnTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Total consultation timeout in seconds (default: 300 = 5 minutes).
    /// </summary>
    public int TotalTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Enable automatic termination on timeout.
    /// </summary>
    public bool AutoTerminateOnTimeout { get; set; } = true;
}

/// <summary>
/// Early termination triggers for consultations.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public enum EarlyTerminationTrigger
{
    /// <summary>
    /// Maximum cost threshold exceeded.
    /// </summary>
    CostThresholdExceeded,

    /// <summary>
    /// Specialist indicates inability to help.
    /// </summary>
    OutOfScope,

    /// <summary>
    /// Malformed response from LLM.
    /// </summary>
    InvalidResponse,

    /// <summary>
    /// API failure or connectivity issue.
    /// </summary>
    ApiFailure,

    /// <summary>
    /// Caller agent cancelled the consultation.
    /// </summary>
    CancelledByCaller
}

/// <summary>
/// Represents the result of a specialist consultation.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class ConsultationResult
{
    /// <summary>
    /// Unique identifier for this consultation session.
    /// </summary>
    public string ConsultationId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the consultation completed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The final answer or recommendation from the specialist.
    /// </summary>
    public string SpecialistResponse { get; set; } = string.Empty;

    /// <summary>
    /// Number of turns taken in the consultation.
    /// </summary>
    public int TurnsTaken { get; set; }

    /// <summary>
    /// Why the consultation ended.
    /// </summary>
    public string TerminationReason { get; set; } = string.Empty;

    /// <summary>
    /// Token usage for this consultation.
    /// </summary>
    public int TotalInputTokens { get; set; }

    /// <summary>
    /// Token usage for this consultation.
    /// </summary>
    public int TotalOutputTokens { get; set; }

    /// <summary>
    /// Total cost in dollars.
    /// </summary>
    public double TotalCost { get; set; }

    /// <summary>
    /// The LLM model used for the specialist.
    /// </summary>
    public string ModelUsed { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the consultation completed.
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the specialist needs more information from the caller.
    /// </summary>
    public bool NeedsMoreInfo { get; set; }

    /// <summary>
    /// Details about what additional information is needed.
    /// </summary>
    public string? InfoRequest { get; set; }

    /// <summary>
    /// List of structured recommendations from the specialist (if any).
    /// </summary>
    public List<StructuredRecommendation>? Recommendations { get; set; }

    /// <summary>
    /// Full message history for debugging/audit.
    /// </summary>
    public List<ConsultationMessage>? MessageHistory { get; set; }
}

/// <summary>
/// Represents the current state of an active consultation session.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class ConsultationSession
{
    /// <summary>
    /// Unique identifier for this consultation session.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// The specialist domain being consulted.
    /// </summary>
    public string SpecialistDomain { get; set; } = string.Empty;

    /// <summary>
    /// The LLM model used for the specialist.
    /// </summary>
    public string SpecialistModel { get; set; } = string.Empty;

    /// <summary>
    /// Messages exchanged in this consultation.
    /// </summary>
    public List<ConsultationMessage> Messages { get; set; } = new();

    /// <summary>
    /// Current turn number.
    /// </summary>
    public int CurrentTurn { get; set; }

    /// <summary>
    /// Number of follow-up messages from the caller.
    /// </summary>
    public int CallerFollowUpCount { get; set; }

    /// <summary>
    /// Number of responses from the specialist.
    /// </summary>
    public int SpecialistResponseCount { get; set; }

    /// <summary>
    /// When the consultation started.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the last activity occurred.
    /// </summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>
    /// Current status of the consultation.
    /// </summary>
    public ConsultationStatus Status { get; set; } = ConsultationStatus.Active;

    /// <summary>
    /// The termination signal used (if any).
    /// </summary>
    public string? TerminationSignal { get; set; }

    /// <summary>
    /// The trigger that caused termination (if applicable).
    /// </summary>
    public EarlyTerminationTrigger? EarlyTerminationTrigger { get; set; }
}

/// <summary>
/// Status of a consultation session.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public enum ConsultationStatus
{
    /// <summary>
    /// Consultation is actively in progress.
    /// </summary>
    Active,

    /// <summary>
    /// Successfully completed with full analysis.
    /// </summary>
    Completed,

    /// <summary>
    /// Timed out during execution.
    /// </summary>
    TimedOut,

    /// <summary>
    /// Maximum turn limit reached.
    /// </summary>
    MaxTurnsReached,

    /// <summary>
    /// Error occurred during consultation.
    /// </summary>
    Error,

    /// <summary>
    /// Cancelled by caller agent.
    /// </summary>
    Cancelled
}
