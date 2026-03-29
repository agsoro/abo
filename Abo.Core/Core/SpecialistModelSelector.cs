using Microsoft.Extensions.Logging;

namespace Abo.Core;

/// <summary>
/// Selects an LLM model for the specialist agent, ensuring it differs from the caller's model.
/// Part of the ConsultSpecialistTool implementation (Issue #407).
/// 
/// This selector leverages the existing OpenRouterModelSelector infrastructure
/// and implements vendor isolation to provide diverse perspectives.
/// </summary>
public class SpecialistModelSelector
{
    private readonly ILogger<SpecialistModelSelector> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Vendor isolation matrix ensures the specialist uses a different vendor than the caller.
    /// This provides perspective diversity for better consultation outcomes.
    /// </summary>
    private static readonly Dictionary<string, string[]> VendorIsolationMatrix = new()
    {
        ["openai"] = ["anthropic", "google", "mistral", "cohere"],
        ["anthropic"] = ["openai", "google", "mistral", "cohere"],
        ["google"] = ["openai", "anthropic", "mistral", "cohere"],
        ["mistral"] = ["openai", "anthropic", "google", "cohere"],
        ["cohere"] = ["openai", "anthropic", "google", "mistral"],
        // Default fallback for unknown vendors
        ["default"] = ["openai", "anthropic", "google", "mistral"]
    };

    /// <summary>
    /// Domain to model priority mappings.
    /// These models are selected when a specific domain is requested.
    /// </summary>
    private static readonly Dictionary<string, (string[] Primary, string[] Fallback)> DomainMappings = new()
    {
        ["architecture"] = (
            Primary: ["anthropic", "google"],
            Fallback: ["openai", "mistral"]
        ),
        ["security"] = (
            Primary: ["anthropic", "openai"],
            Fallback: ["google", "mistral"]
        ),
        ["performance"] = (
            Primary: ["google", "anthropic"],
            Fallback: ["openai", "mistral"]
        ),
        ["database"] = (
            Primary: ["google", "openai"],
            Fallback: ["anthropic", "mistral"]
        ),
        ["frontend"] = (
            Primary: ["anthropic", "openai"],
            Fallback: ["google", "mistral"]
        ),
        ["backend"] = (
            Primary: ["openai", "google"],
            Fallback: ["anthropic", "mistral"]
        ),
        ["devops"] = (
            Primary: ["google", "anthropic"],
            Fallback: ["openai", "mistral"]
        ),
        ["testing"] = (
            Primary: ["openai", "anthropic"],
            Fallback: ["google", "mistral"]
        ),
        ["implementation"] = (
            Primary: ["openai", "anthropic"],
            Fallback: ["google", "mistral"]
        ),
        ["general"] = (
            Primary: ["anthropic", "google", "openai"],
            Fallback: ["mistral", "cohere"]
        )
    };

    public SpecialistModelSelector(
        ILogger<SpecialistModelSelector> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Selects an appropriate LLM model for the specialist based on the caller's model
    /// and optional domain hint.
    /// </summary>
    /// <param name="callerModel">The model used by the calling agent.</param>
    /// <param name="domain">Optional domain hint for specialized model selection.</param>
    /// <returns>The selected specialist model identifier.</returns>
    public string SelectModel(string callerModel, string? domain = null)
    {
        var strategy = _configuration["Consultation:ModelSelectionStrategy"] ?? "vendor_isolation";

        return strategy.ToLowerInvariant() switch
        {
            "vendor_isolation" => SelectModelWithVendorIsolation(callerModel, domain),
            "cheapest" => SelectCheapestModel(domain),
            "round_robin" => SelectRoundRobinModel(callerModel, domain),
            "domain_priority" => SelectDomainPriorityModel(callerModel, domain),
            _ => SelectModelWithVendorIsolation(callerModel, domain)
        };
    }

    /// <summary>
    /// Default strategy: Vendor isolation ensures specialist uses different vendor.
    /// </summary>
    private string SelectModelWithVendorIsolation(string callerModel, string? domain)
    {
        var callerVendor = ExtractVendor(callerModel);
        var allowedVendors = VendorIsolationMatrix.GetValueOrDefault(callerVendor, VendorIsolationMatrix["default"]);

        _logger.LogInformation($"[SpecialistModelSelector] Vendor isolation: caller={callerVendor}, allowed={string.Join(", ", allowedVendors)}");

        // First try to use CapableModelName if configured
        var capableModel = _configuration["Config:CapableModelName"];
        if (!string.IsNullOrEmpty(capableModel))
        {
            var capableVendor = ExtractVendor(capableModel);
            if (allowedVendors.Contains(capableVendor, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"[SpecialistModelSelector] Selected CapableModel (vendor isolation): {capableModel}");
                return capableModel;
            }
        }

        // Try domain-mapped models first
        if (!string.IsNullOrEmpty(domain))
        {
            var selectedModel = SelectFromVendorList(allowedVendors, domain);
            if (!string.IsNullOrEmpty(selectedModel))
            {
                return selectedModel;
            }
        }

        // Fallback to ReviewModelName if available
        var reviewModel = _configuration["Config:ReviewModelName"];
        if (!string.IsNullOrEmpty(reviewModel))
        {
            var reviewVendor = ExtractVendor(reviewModel);
            if (allowedVendors.Contains(reviewVendor, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"[SpecialistModelSelector] Selected ReviewModel (vendor isolation): {reviewModel}");
                return reviewModel;
            }
        }

        // Final fallback to default model (may not satisfy isolation)
        var defaultModel = _configuration["Config:ModelName"] ?? "anthropic/claude-3-haiku";
        _logger.LogWarning($"[SpecialistModelSelector] Using fallback model (vendor isolation may not be satisfied): {defaultModel}");
        return defaultModel;
    }

    /// <summary>
    /// Cheapest model selection strategy.
    /// </summary>
    private string SelectCheapestModel(string? domain)
    {
        // Check configured specialist models for cheapest option
        var specialistModels = _configuration["Consultation:SpecialistModels"]?.Split(',', StringSplitOptions.RemoveEmptyEntries);

        if (specialistModels != null && specialistModels.Length > 0)
        {
            // For now, just return the first configured model
            // In production, would compare prices from pricing data
            _logger.LogInformation($"[SpecialistModelSelector] Selected cheapest configured model: {specialistModels[0].Trim()}");
            return specialistModels[0].Trim();
        }

        // Fallback to capable model
        var capableModel = _configuration["Config:CapableModelName"];
        if (!string.IsNullOrEmpty(capableModel))
        {
            return capableModel;
        }

        return _configuration["Config:ModelName"] ?? "anthropic/claude-3-haiku";
    }

    /// <summary>
    /// Round-robin selection across available models.
    /// </summary>
    private string SelectRoundRobinModel(string callerModel, string? domain)
    {
        // Simple round-robin using configured models
        var specialistModels = _configuration["Consultation:SpecialistModels"]?.Split(',', StringSplitOptions.RemoveEmptyEntries);

        if (specialistModels != null && specialistModels.Length > 0)
        {
            // Use a simple hash-based selection for distribution
            var hash = Environment.TickCount % specialistModels.Length;
            var selected = specialistModels[Math.Abs(hash)].Trim();
            _logger.LogInformation($"[SpecialistModelSelector] Round-robin selected: {selected}");
            return selected;
        }

        return SelectModelWithVendorIsolation(callerModel, domain);
    }

    /// <summary>
    /// Domain priority selection - prefers models optimized for the domain.
    /// </summary>
    private string SelectDomainPriorityModel(string callerModel, string? domain)
    {
        if (string.IsNullOrEmpty(domain))
        {
            return SelectModelWithVendorIsolation(callerModel, "general");
        }

        var normalizedDomain = domain.ToLowerInvariant();
        if (!DomainMappings.TryGetValue(normalizedDomain, out var mapping))
        {
            normalizedDomain = "general";
            mapping = DomainMappings["general"];
        }

        var callerVendor = ExtractVendor(callerModel);

        // Try primary vendors for the domain
        foreach (var vendor in mapping.Primary)
        {
            if (vendor.Equals(callerVendor, StringComparison.OrdinalIgnoreCase))
                continue; // Skip same vendor as caller

            var model = GetModelForVendor(vendor);
            if (!string.IsNullOrEmpty(model))
            {
                _logger.LogInformation($"[SpecialistModelSelector] Domain priority ({normalizedDomain}): {model}");
                return model;
            }
        }

        // Try fallback vendors
        foreach (var vendor in mapping.Fallback)
        {
            if (vendor.Equals(callerVendor, StringComparison.OrdinalIgnoreCase))
                continue;

            var model = GetModelForVendor(vendor);
            if (!string.IsNullOrEmpty(model))
            {
                _logger.LogInformation($"[SpecialistModelSelector] Domain priority fallback ({normalizedDomain}): {model}");
                return model;
            }
        }

        return SelectModelWithVendorIsolation(callerModel, domain);
    }

    /// <summary>
    /// Selects a model from a list of allowed vendors.
    /// </summary>
    private string SelectFromVendorList(string[] allowedVendors, string? domain)
    {
        foreach (var vendor in allowedVendors)
        {
            var model = GetModelForVendor(vendor);
            if (!string.IsNullOrEmpty(model))
            {
                _logger.LogInformation($"[SpecialistModelSelector] Vendor list selection ({domain ?? "general"}): {model}");
                return model;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets a model identifier for a given vendor from configuration.
    /// </summary>
    private string? GetModelForVendor(string vendor)
    {
        // Check domain-specific mappings first
        var domainConfigKey = $"Consultation:DomainMappings:{vendor}";
        var domainModel = _configuration[domainConfigKey];
        if (!string.IsNullOrEmpty(domainModel))
        {
            return domainModel;
        }

        // Check configured specialist models
        var specialistModels = _configuration["Consultation:SpecialistModels"]?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (specialistModels != null)
        {
            foreach (var model in specialistModels)
            {
                var modelVendor = ExtractVendor(model);
                if (modelVendor.Equals(vendor, StringComparison.OrdinalIgnoreCase))
                {
                    return model.Trim();
                }
            }
        }

        // Fallback to configured model names
        return vendor.ToLowerInvariant() switch
        {
            "anthropic" => _configuration["Config:CapableModelName"]?.Contains("anthropic") == true
                ? _configuration["Config:CapableModelName"]
                : _configuration["Config:ReviewModelName"]?.Contains("anthropic") == true
                    ? _configuration["Config:ReviewModelName"]
                    : null,
            "openai" => _configuration["Config:ModelName"]?.Contains("openai") == true
                ? _configuration["Config:ModelName"]
                : null,
            "google" => _configuration["Config:CapableModelName"]?.Contains("google") == true
                ? _configuration["Config:CapableModelName"]
                : _configuration["Config:ModelName"]?.Contains("google") == true
                    ? _configuration["Config:ModelName"]
                    : null,
            _ => null
        };
    }

    /// <summary>
    /// Extracts the vendor name from a model identifier.
    /// </summary>
    private static string ExtractVendor(string model)
    {
        if (string.IsNullOrEmpty(model))
            return "default";

        var lowerModel = model.ToLowerInvariant();

        if (lowerModel.StartsWith("anthropic/"))
            return "anthropic";
        if (lowerModel.StartsWith("openai/"))
            return "openai";
        if (lowerModel.StartsWith("google/"))
            return "google";
        if (lowerModel.StartsWith("mistral/"))
            return "mistral";
        if (lowerModel.StartsWith("cohere/"))
            return "cohere";

        // Try to infer from known model names
        if (lowerModel.Contains("claude"))
            return "anthropic";
        if (lowerModel.Contains("gpt"))
            return "openai";
        if (lowerModel.Contains("gemini"))
            return "google";

        return "default";
    }

    /// <summary>
    /// Validates that the selected specialist model is different from the caller's model.
    /// </summary>
    public bool IsValidSelection(string specialistModel, string callerModel)
    {
        var specialistVendor = ExtractVendor(specialistModel);
        var callerVendor = ExtractVendor(callerModel);

        var isDifferent = !specialistVendor.Equals(callerVendor, StringComparison.OrdinalIgnoreCase);

        if (!isDifferent)
        {
            _logger.LogWarning($"[SpecialistModelSelector] Invalid selection: specialist={specialistModel} has same vendor as caller={callerModel}");
        }

        return isDifferent;
    }

    /// <summary>
    /// Gets a list of all available vendors from configuration.
    /// </summary>
    public IReadOnlyList<string> GetAvailableVendors()
    {
        var vendors = new HashSet<string>();

        var modelName = _configuration["Config:ModelName"];
        if (!string.IsNullOrEmpty(modelName))
            vendors.Add(ExtractVendor(modelName));

        var capableModel = _configuration["Config:CapableModelName"];
        if (!string.IsNullOrEmpty(capableModel))
            vendors.Add(ExtractVendor(capableModel));

        var reviewModel = _configuration["Config:ReviewModelName"];
        if (!string.IsNullOrEmpty(reviewModel))
            vendors.Add(ExtractVendor(reviewModel));

        return vendors.ToList();
    }
}
