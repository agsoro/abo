using Abo.Core.Models;

namespace Abo.Core;

/// <summary>
/// Interface for the Orchestrator that manages specialist consultations.
/// This abstraction enables proper unit testing of ConsultationService
/// without requiring all Orchestrator dependencies.
/// </summary>
public interface IOrchestrator
{
    /// <summary>
    /// Runs a specialist consultation loop. The specialist agent runs on a different LLM
    /// than the caller, has no tools, and communicates in a turn-based protocol.
    /// </summary>
    /// <param name="request">The consultation request details.</param>
    /// <returns>The result of the consultation.</returns>
    Task<ConsultationResult> RunConsultationAsync(ConsultationRequest request);
}
