namespace Abo.Core;

/// <summary>
/// Shared sentinel prefix constants used to signal special agent lifecycle events
/// through tool result strings. The Orchestrator detects these to short-circuit
/// the agent loop without extra LLM round-trips.
/// </summary>
public static class AgentSentinels
{
    /// <summary>
    /// Prefix returned by SpecialistAgent.conclude_step on success.
    /// The Orchestrator detects this and immediately returns the resultNotes
    /// to the caller, eliminating an unnecessary LLM synthesis round-trip.
    /// Format: [CONCLUDE_STEP_RESULT]:{resultNotes}
    /// </summary>
    public const string ConcludeStepResult = "[CONCLUDE_STEP_RESULT]:";

    /// <summary>
    /// Prefix returned by SpecialistAgent.postpone_task.
    /// The Orchestrator detects this and immediately returns the contextNotes
    /// to the caller without advancing the workflow step.
    /// Format: [POSTPONE_TASK_RESULT]:{contextNotes}
    /// </summary>
    public const string PostponeTaskResult = "[POSTPONE_TASK_RESULT]:";

    /// <summary>
    /// Signal used by the specialist agent to indicate the consultation is complete.
    /// Format: [CONSULTATION_COMPLETE]
    /// </summary>
    public const string ConsultationComplete = "[CONSULTATION_COMPLETE]";

    /// <summary>
    /// Signal used by the specialist agent to indicate more information is needed.
    /// Format: [NEEDS_MORE_INFO]
    /// </summary>
    public const string NeedsMoreInfo = "[NEEDS_MORE_INFO]";

    /// <summary>
    /// Sentinel marker for the Orchestrator to detect consultation termination.
    /// </summary>
    public const string ConsultationTerminate = "[CONSULTATION_TERMINATE]";
}
