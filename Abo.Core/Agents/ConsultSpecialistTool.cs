using System.Text.Json;
using System.Text.Json.Serialization;
using Abo.Core.Models;
using Abo.Core.Services;
using Abo.Tools;

namespace Abo.Agents;

/// <summary>
/// Tool parameters for the ConsultSpecialist tool.
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
/// The specialist runs on a different LLM than the caller, generates its own system prompt,
/// and provides expert consultation without using any tools.
/// </summary>
public class ConsultSpecialistTool : IAboTool
{
    private readonly IConsultationService _consultationService;

    public string Name => "consult_specialist";
    public string Description => "Consult an expert specialist agent for complex tasks. Use this when you need specialized expertise, a fresh perspective, or when a task requires knowledge beyond your current context. The specialist will analyze the task and provide recommendations.";

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
                description = "Optional domain/specialty for the specialist (e.g., 'architecture', 'security', 'performance', 'database', 'frontend', 'backend'). If not provided, a generalist will be selected."
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

            // Create consultation request
            var request = new ConsultationRequest
            {
                CallerAgentName = "ManagerAgent",
                SpecialistDomain = parameters.SpecialistDomain,
                TaskDescription = parameters.TaskDescription,
                ContextSummary = parameters.ContextSummary
            };

            // Run the consultation
            var result = await _consultationService.RunConsultationAsync(request);

            if (result == null)
            {
                return "[ERROR] Consultation failed to produce a result.";
            }

            // Return the specialist's response
            if (result.NeedsMoreInfo)
            {
                return $"[SPECIALIST_NEEDS_MORE_INFO]\n{result.InfoRequest}\n\n{result.SpecialistResponse}";
            }

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
