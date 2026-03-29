using Abo.Core.Models;
using Microsoft.Extensions.Logging;

namespace Abo.Core;

/// <summary>
/// Determines when to suggest specialist consultation based on conversation metrics.
/// Part of the ConsultSpecialistTool implementation (Issue #407).
/// 
/// This trigger integrates with existing Orchestrator summarization triggers and
/// provides configurable thresholds for message count, cost, and loop percentage.
/// </summary>
public class ConsultationTrigger
{
    private readonly ILogger<ConsultationTrigger> _logger;
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Default thresholds for triggering specialist consultation suggestions.
    /// </summary>
    public static class Defaults
    {
        /// <summary>
        /// Default message count threshold (100 messages).
        /// </summary>
        public const int MessageCount = 100;

        /// <summary>
        /// Default cost threshold in dollars ($0.80).
        /// </summary>
        public const double Cost = 0.80;

        /// <summary>
        /// Default loop percentage threshold (90% of max loops).
        /// </summary>
        public const double LoopPercentage = 90.0;

        /// <summary>
        /// Default warning threshold percentage (80% of max loops).
        /// </summary>
        public const double WarningPercentage = 80.0;

        /// <summary>
        /// Default maximum loops (200).
        /// </summary>
        public const int MaxLoops = 200;
    }

    public ConsultationTrigger(
        ILogger<ConsultationTrigger> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the configured message count threshold.
    /// </summary>
    public int MessageCountThreshold =>
        int.TryParse(_configuration["Consultation:Triggers:MessageCount"], out var msg)
            ? msg
            : Defaults.MessageCount;

    /// <summary>
    /// Gets the configured cost threshold in dollars.
    /// </summary>
    public double CostThreshold =>
        double.TryParse(_configuration["Consultation:Triggers:Cost"], out var cost)
            ? cost
            : Defaults.Cost;

    /// <summary>
    /// Gets the configured loop percentage threshold.
    /// </summary>
    public double LoopPercentageThreshold =>
        double.TryParse(_configuration["Consultation:Triggers:LoopPercentage"], out var loop)
            ? loop
            : Defaults.LoopPercentage;

    /// <summary>
    /// Gets the configured warning percentage threshold.
    /// </summary>
    public double WarningPercentageThreshold =>
        double.TryParse(_configuration["Consultation:Triggers:WarningPercentage"], out var warn)
            ? warn
            : Defaults.WarningPercentage;

    /// <summary>
    /// Gets the configured maximum loops.
    /// </summary>
    public int MaxLoops =>
        int.TryParse(_configuration["Consultation:Triggers:MaxLoops"], out var max)
            ? max
            : Defaults.MaxLoops;

    /// <summary>
    /// Checks if specialist consultation should be suggested based on current metrics.
    /// </summary>
    /// <param name="metrics">Current agent metrics.</param>
    /// <returns>Trigger result with recommendation.</returns>
    public ConsultationTriggerResult Evaluate(AgentMetrics metrics)
    {
        var result = new ConsultationTriggerResult();

        // Check message count threshold
        if (metrics.MessageCount >= MessageCountThreshold)
        {
            result.ShouldSuggest = true;
            result.Reasons.Add($"Message count ({metrics.MessageCount}) exceeded threshold ({MessageCountThreshold})");
            result.SuggestionType = SuggestionType.MessageCount;
            _logger.LogInformation($"[ConsultationTrigger] Message count threshold reached: {metrics.MessageCount} >= {MessageCountThreshold}");
        }

        // Check cost threshold
        if (metrics.AccumulatedCost >= CostThreshold)
        {
            result.ShouldSuggest = true;
            result.Reasons.Add($"Accumulated cost (${metrics.AccumulatedCost:F4}) exceeded threshold (${CostThreshold:F2})");
            result.SuggestionType = SuggestionType.Cost;
            _logger.LogInformation($"[ConsultationTrigger] Cost threshold reached: ${metrics.AccumulatedCost:F4} >= ${CostThreshold:F2}");
        }

        // Check loop percentage threshold
        var loopPercentage = MaxLoops > 0 ? (metrics.CurrentLoop / (double)MaxLoops) * 100 : 0;
        if (loopPercentage >= LoopPercentageThreshold)
        {
            result.ShouldSuggest = true;
            result.Reasons.Add($"Loop percentage ({loopPercentage:F1}%) exceeded threshold ({LoopPercentageThreshold}%)");
            result.SuggestionType = SuggestionType.LoopPercentage;
            _logger.LogInformation($"[ConsultationTrigger] Loop percentage threshold reached: {loopPercentage:F1}% >= {LoopPercentageThreshold}%");
        }

        // Check if we're in a loop (repeated similar responses)
        if (metrics.LoopPercentage >= Defaults.LoopPercentage)
        {
            result.ShouldSuggest = true;
            result.Reasons.Add($"Loop detection ({metrics.LoopPercentage:F1}%) indicates repetitive behavior");
            result.SuggestionType = SuggestionType.LoopDetection;
            _logger.LogInformation($"[ConsultationTrigger] Loop detection threshold reached: {metrics.LoopPercentage:F1}%");
        }

        // Calculate how close we are to each threshold
        result.MessageCountProximity = metrics.MessageCount / (double)MessageCountThreshold;
        result.CostProximity = metrics.AccumulatedCost / CostThreshold;
        result.LoopPercentageProximity = loopPercentage / LoopPercentageThreshold;

        return result;
    }

    /// <summary>
    /// Checks if a warning should be issued (earlier than full suggestion).
    /// </summary>
    /// <param name="metrics">Current agent metrics.</param>
    /// <returns>Warning message if thresholds are approaching, null otherwise.</returns>
    public string? GetWarningMessage(AgentMetrics metrics)
    {
        var warnings = new List<string>();

        // Check message count proximity (80% threshold)
        var msgWarningThreshold = MessageCountThreshold * (WarningPercentageThreshold / 100.0);
        if (metrics.MessageCount >= msgWarningThreshold && metrics.MessageCount < MessageCountThreshold)
        {
            warnings.Add($"Approaching message limit ({metrics.MessageCount}/{MessageCountThreshold})");
        }

        // Check cost proximity
        var costWarningThreshold = CostThreshold * (WarningPercentageThreshold / 100.0);
        if (metrics.AccumulatedCost >= costWarningThreshold && metrics.AccumulatedCost < CostThreshold)
        {
            warnings.Add($"Approaching cost limit (${metrics.AccumulatedCost:F4}/${CostThreshold:F2})");
        }

        // Check loop percentage proximity
        var loopPercentage = MaxLoops > 0 ? (metrics.CurrentLoop / (double)MaxLoops) * 100 : 0;
        var loopWarningThreshold = LoopPercentageThreshold * (WarningPercentageThreshold / 100.0);
        if (loopPercentage >= loopWarningThreshold && loopPercentage < LoopPercentageThreshold)
        {
            warnings.Add($"Approaching loop limit ({loopPercentage:F1}%/{LoopPercentageThreshold}%)");
        }

        if (warnings.Count == 0)
            return null;

        return $"⚠️ Consider using specialist consultation: {string.Join(", ", warnings)}";
    }

    /// <summary>
    /// Generates a suggestion message for using the specialist tool.
    /// </summary>
    /// <param name="result">The trigger evaluation result.</param>
    /// <returns>A formatted suggestion message.</returns>
    public string GetSuggestionMessage(ConsultationTriggerResult result)
    {
        if (!result.ShouldSuggest)
            return string.Empty;

        var primaryReason = result.Reasons.FirstOrDefault() ?? "Threshold reached";
        var domainHint = SuggestDomainBasedOnMetrics(result.SuggestionType);

        return $@"## Specialist Consultation Suggested

**Reason:** {primaryReason}

**Recommendation:** Consider consulting a specialist for a fresh perspective or specialized expertise.

You can use the `consult_specialist` tool to get expert advice on complex aspects of this task.

**Suggested domain:** {domainHint}

This can help:
- Break through complex technical challenges
- Get specialized knowledge for unfamiliar domains
- Reduce accumulated cost by getting targeted advice
- Avoid repetitive loops by leveraging fresh perspective

Example usage:
```
consult_specialist(
  taskDescription=""Describe the specific task or question"",
  contextSummary=""Summarize the current context and what's been tried"",
  specialistDomain=""{domainHint}""  // optional
)
```";
    }

    /// <summary>
    /// Suggests a domain based on the type of trigger that fired.
    /// </summary>
    private static string SuggestDomainBasedOnMetrics(SuggestionType suggestionType)
    {
        return suggestionType switch
        {
            SuggestionType.MessageCount => "architecture",
            SuggestionType.Cost => "implementation",
            SuggestionType.LoopPercentage => "architecture",
            SuggestionType.LoopDetection => "architecture",
            _ => "general"
        };
    }

    /// <summary>
    /// Creates a default metrics object for a new session.
    /// </summary>
    public static AgentMetrics CreateInitialMetrics()
    {
        return new AgentMetrics
        {
            SessionId = Guid.NewGuid().ToString("N")[..8],
            MessageCount = 0,
            AccumulatedCost = 0.0,
            CurrentLoop = 0,
            LoopPercentage = 0.0,
            StartTime = DateTime.UtcNow,
            TokenUsage = new TokenUsage()
        };
    }
}

/// <summary>
/// Represents the result of evaluating consultation triggers.
/// </summary>
public class ConsultationTriggerResult
{
    /// <summary>
    /// Whether specialist consultation should be suggested.
    /// </summary>
    public bool ShouldSuggest { get; set; }

    /// <summary>
    /// The primary type of suggestion triggered.
    /// </summary>
    public SuggestionType SuggestionType { get; set; }

    /// <summary>
    /// List of reasons why the trigger fired.
    /// </summary>
    public List<string> Reasons { get; set; } = new();

    /// <summary>
    /// Proximity to message count threshold (0.0 to 1.0+).
    /// </summary>
    public double MessageCountProximity { get; set; }

    /// <summary>
    /// Proximity to cost threshold (0.0 to 1.0+).
    /// </summary>
    public double CostProximity { get; set; }

    /// <summary>
    /// Proximity to loop percentage threshold (0.0 to 1.0+).
    /// </summary>
    public double LoopPercentageProximity { get; set; }
}

/// <summary>
/// Types of triggers that can suggest specialist consultation.
/// </summary>
public enum SuggestionType
{
    /// <summary>
    /// Message count threshold reached.
    /// </summary>
    MessageCount,

    /// <summary>
    /// Cost threshold reached.
    /// </summary>
    Cost,

    /// <summary>
    /// Loop percentage threshold reached.
    /// </summary>
    LoopPercentage,

    /// <summary>
    /// Loop detection (repetitive behavior).
    /// </summary>
    LoopDetection,

    /// <summary>
    /// Manual trigger by agent.
    /// </summary>
    Manual
}
