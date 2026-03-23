namespace Abo.Core;

/// <summary>
/// Shared sentinel prefix constants used to signal special agent lifecycle events
/// through tool result strings. The Orchestrator detects these to short-circuit
/// the agent loop without extra LLM round-trips.
/// </summary>
public static class AgentSentinels
{
    /// <summary>
    /// Prefix returned by SpecialistAgent.complete_task on success.
    /// The Orchestrator detects this and immediately returns the resultNotes
    /// to the caller, eliminating an unnecessary LLM synthesis round-trip.
    /// Format: [COMPLETE_TASK_RESULT]:{resultNotes}
    /// </summary>
    public const string CompleteTaskResult = "[COMPLETE_TASK_RESULT]:";
}
