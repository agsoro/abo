namespace Abo.Core.Models;

/// <summary>
/// Represents a consultation request between an agent and a specialist.
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
}

/// <summary>
/// Represents a single message in a specialist consultation exchange.
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
    /// The content of the message.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the message.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the result of a specialist consultation.
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
}
