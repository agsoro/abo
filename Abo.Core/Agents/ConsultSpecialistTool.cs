using System.Text.Json;
using System.Text.Json.Serialization;
using Abo.Core.Models;
using Abo.Core.Services;
using Abo.Tools;

namespace Abo.Agents;

/// <summary>
/// Tool parameters for the ConsultSpecialist tool.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
public class ConsultSpecialistParameters
{
    [JsonPropertyName("taskDescription")]
    public string TaskDescription { get; set; } = string.Empty;

    [JsonPropertyName("contextSummary")]
    public string ContextSummary { get; set; } = string.Empty;

    [JsonPropertyName("specialistDomain")]
    public string? SpecialistDomain { get; set; }
}

/// <summary>
/// Tool that enables agents to consult a specialist agent for complex tasks.
/// Implements the ConsultSpecialistTool interface defined in Issue #407.
/// 
/// The specialist runs on a different LLM than the caller, generates its own system prompt,
/// and provides expert consultation using a structured message protocol.
///
/// Key Protocol Features (Issue #406):
/// - Turn-based conversation with configurable limits (max 5 turns)
/// - Explicit termination signals ([CONSULTATION_COMPLETE], [CONCLUSION], [NEEDS_MORE_INFO])
/// - Results returned without re-validation (divide-and-conquer trust model)
/// - Consultation history isolated from main task context
/// </summary>
public class ConsultSpecialistTool : IAboTool
{
    private readonly IConsultationService _consultationService;

    public string Name => "consult_specialist";
    public string Description => "Consult an expert specialist agent for complex tasks. Use this when you need specialized expertise, a fresh perspective, or when a task requires knowledge beyond your current context. The specialist will analyze the task and provide recommendations using a structured consultation protocol.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            taskDescription = new
            {
                type = "string",
                description = "A detailed description of the task or question to consult the specialist about. Be specific and comprehensive."
            },
            contextSummary = new
            {
                type = "string",
                description = "A comprehensive summary of the broader context, including any relevant constraints, existing solutions, requirements, or background information that would help the specialist provide better advice."
            },
            specialistDomain = new
            {
                type = "string",
                description = "Optional domain/specialty for the specialist (e.g., 'architecture', 'security', 'performance', 'database', 'frontend', 'backend', 'implementation'). If not provided, a generalist will be selected."
            }
        },
        required = new[] { "taskDescription", "contextSummary" }
    };

    public ConsultSpecialistTool(IConsultationService consultationService)
    {
        _consultationService = consultationService;
    }

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var parameters = JsonSerializer.Deserialize<ConsultSpecialistParameters>(argumentsJson);

            if (parameters == null)
            {
                return "[ERROR] Failed to parse tool arguments.";
            }

            if (string.IsNullOrWhiteSpace(parameters.TaskDescription))
            {
                return "[ERROR] Task description is required.";
            }

            if (string.IsNullOrWhiteSpace(parameters.ContextSummary))
            {
                return "[ERROR] Context summary is required.";
            }

            // Create consultation request per the protocol (Issue #406)
            var request = new ConsultationRequest
            {
                CallerAgentName = "ManagerAgent",
                SpecialistDomain = parameters.SpecialistDomain,
                TaskDescription = parameters.TaskDescription,
                ContextSummary = parameters.ContextSummary
            };

            // Run the consultation using the ConsultationService
            // This handles the turn-based protocol with termination signals
            var result = await _consultationService.RunConsultationAsync(request);

            if (result == null)
            {
                return "[ERROR] Consultation failed to produce a result.";
            }

            // Return the specialist's response following the protocol
            // The response is already cleaned of internal markers by the Orchestrator
            if (result.NeedsMoreInfo)
            {
                // Specialist needs more info but couldn't get it - return partial result
                return $"[SPECIALIST_NEEDS_MORE_INFO]\n{result.InfoRequest}\n\n{result.SpecialistResponse}";
            }

            // Return successful consultation result
            // Format: [SPECIALIST_CONSULTATION_COMPLETE] followed by the response
            return $"[SPECIALIST_CONSULTATION_COMPLETE]\n{result.SpecialistResponse}";
        }
        catch (JsonException ex)
        {
            return $"[ERROR] Invalid JSON arguments: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"[ERROR] Consultation failed: {ex.Message}";
        }
    }
}
