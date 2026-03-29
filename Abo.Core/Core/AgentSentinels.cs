namespace Abo.Core;

/// <summary>
/// Shared sentinel prefix constants used to signal special agent lifecycle events
/// through tool result strings. The Orchestrator detects these to short-circuit
/// the agent loop without extra LLM round-trips.
/// 
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public static class AgentSentinels
{
    #region Agent Lifecycle Sentinels

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

    #endregion

    #region Consultation Protocol Sentinels (Issue #406)

    /// <summary>
    /// Signal used by the consultation specialist to indicate the consultation is complete.
    /// The caller agent should proceed with the specialist's recommendations.
    /// Format: [CONSULTATION_COMPLETE]
    /// </summary>
    public const string ConsultationComplete = "[CONSULTATION_COMPLETE]";

    /// <summary>
    /// Signal used by the consultation specialist to indicate more information is needed
    /// before providing a final recommendation.
    /// Format: [NEEDS_MORE_INFO]
    /// </summary>
    public const string NeedsMoreInfo = "[NEEDS_MORE_INFO]";

    /// <summary>
    /// Signal used by the consultation specialist to indicate a final recommendation
    /// with explicit end marker.
    /// Format: [CONCLUSION]
    /// </summary>
    public const string Conclusion = "[CONCLUSION]";

    /// <summary>
    /// Sentinel marker for the Orchestrator to detect consultation termination.
    /// Format: [CONSULTATION_TERMINATE]
    /// </summary>
    public const string ConsultationTerminate = "[CONSULTATION_TERMINATE]";

    /// <summary>
    /// Signal indicating the specialist encountered an out-of-scope request.
    /// Format: [OUT_OF_SCOPE]
    /// </summary>
    public const string OutOfScope = "[OUT_OF_SCOPE]";

    /// <summary>
    /// Signal indicating a timeout occurred during consultation.
    /// Format: [TIMEOUT]
    /// </summary>
    public const string Timeout = "[TIMEOUT]";

    /// <summary>
    /// Signal indicating maximum turn limit was reached.
    /// Format: [MAX_TURNS]
    /// </summary>
    public const string MaxTurns = "[MAX_TURNS]";

    #endregion

    #region Tool Result Sentinels

    /// <summary>
    /// Prefix for consultation results returned to the caller.
    /// Format: [SPECIALIST_CONSULTATION_COMPLETE]:{response}
    /// </summary>
    public const string SpecialistConsultationComplete = "[SPECIALIST_CONSULTATION_COMPLETE]";

    /// <summary>
    /// Prefix when specialist needs more information.
    /// Format: [SPECIALIST_NEEDS_MORE_INFO]:{infoRequest}
    /// </summary>
    public const string SpecialistNeedsMoreInfo = "[SPECIALIST_NEEDS_MORE_INFO]";

    #endregion
}
