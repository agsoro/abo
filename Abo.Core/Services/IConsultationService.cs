using Abo.Core.Models;

namespace Abo.Core.Services;

/// <summary>
/// Interface for running specialist consultations.
/// Part of the Consultation Message Protocol (Issue #406).
/// 
/// This abstraction allows agents to request specialist consultation without direct Orchestrator dependency.
/// The consultation follows a structured turn-based protocol with explicit termination signals.
/// </summary>
public interface IConsultationService
{
    /// <summary>
    /// Runs a specialist consultation loop. The specialist agent runs on a different LLM
    /// than the caller, has no tools, and communicates in a turn-based protocol.
    /// </summary>
    /// <param name="request">The consultation request details.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The result of the consultation.</returns>
    Task<ConsultationResult> RunConsultationAsync(ConsultationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current state of a consultation session.
    /// </summary>
    /// <param name="consultationId">The consultation ID.</param>
    /// <returns>The session state, or null if not found.</returns>
    ConsultationSession? GetSession(string consultationId);

    /// <summary>
    /// Terminates an active consultation early.
    /// </summary>
    /// <param name="consultationId">The consultation ID.</param>
    /// <param name="trigger">The reason for early termination.</param>
    /// <param name="reason">Optional additional reason.</param>
    Task TerminateAsync(string consultationId, EarlyTerminationTrigger trigger, string? reason = null);
}

/// <summary>
/// Default implementation of the consultation service that delegates to the Orchestrator.
/// </summary>
public class ConsultationService : IConsultationService
{
    private readonly IOrchestrator _orchestrator;

    public ConsultationService(IOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public async Task<ConsultationResult> RunConsultationAsync(ConsultationRequest request, CancellationToken cancellationToken = default)
    {
        return await _orchestrator.RunConsultationAsync(request);
    }

    public ConsultationSession? GetSession(string consultationId)
    {
        // Currently, sessions are tracked within the Orchestrator's RunConsultationAsync method
        // This method can be extended to store sessions in a shared state
        return null;
    }

    public Task TerminateAsync(string consultationId, EarlyTerminationTrigger trigger, string? reason = null)
    {
        // Currently, termination is handled within the RunConsultationAsync loop
        // This method can be extended to support external termination signals
        return Task.CompletedTask;
    }
}
