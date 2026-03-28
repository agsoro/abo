using Abo.Core.Models;

namespace Abo.Core.Services;

/// <summary>
/// Interface for running specialist consultations.
/// This abstraction allows agents to request specialist consultation without direct Orchestrator dependency.
/// </summary>
public interface IConsultationService
{
    /// <summary>
    /// Runs a specialist consultation loop. The specialist agent runs on a different LLM
    /// than the caller, has no tools, and communicates in a turn-based protocol.
    /// </summary>
    /// <param name="request">The consultation request details.</param>
    /// <returns>The result of the consultation.</returns>
    Task<ConsultationResult> RunConsultationAsync(ConsultationRequest request);
}

/// <summary>
/// Default implementation of the consultation service that delegates to the Orchestrator.
/// </summary>
public class ConsultationService : IConsultationService
{
    private readonly Orchestrator _orchestrator;

    public ConsultationService(Orchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<ConsultationResult> RunConsultationAsync(ConsultationRequest request)
    {
        return _orchestrator.RunConsultationAsync(request);
    }
}
