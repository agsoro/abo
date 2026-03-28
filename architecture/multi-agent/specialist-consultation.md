# Multi-Agent Specialist Consultation Architecture

## Overview

This document describes the architecture for the Specialist Consultation System, which enables agents to consult specialized experts when handling complex tasks. The system is designed to reduce lengthy conversations, enable divide-and-conquer problem solving, and leverage different LLM perspectives.

## Architecture Components

### 1. ConsultationRequest (`Abo.Core/Models/ConsultationModels.cs`)

Represents a consultation request between an agent and a specialist.

```csharp
public class ConsultationRequest
{
    public string ConsultationId { get; set; }          // Unique ID
    public string CallerAgentName { get; set; }         // "ManagerAgent", etc.
    public string? SpecialistDomain { get; set; }        // "architecture", "security", etc.
    public string TaskDescription { get; set; }          // Task to consult on
    public string ContextSummary { get; set; }           // Broader context
    public string? ParentSessionId { get; set; }        // For tracking
    public string? IssueId { get; set; }                 // Issue tracking
    public DateTime RequestedAt { get; set; }
}
```

### 2. ConsultationResult (`Abo.Core/Models/ConsultationModels.cs`)

Represents the result of a consultation.

```csharp
public class ConsultationResult
{
    public string ConsultationId { get; set; }
    public bool Success { get; set; }
    public string SpecialistResponse { get; set; }       // Final answer/recommendation
    public int TurnsTaken { get; set; }
    public string TerminationReason { get; set; }        // Why consultation ended
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public double TotalCost { get; set; }
    public string ModelUsed { get; set; }
    public bool NeedsMoreInfo { get; set; }
    public string? InfoRequest { get; set; }
}
```

### 3. SpecialistAgent (`Abo.Core/Agents/SpecialistAgent.cs`)

A specialist agent with the following characteristics:

- **No tools** - Pure advisory role, focused only on providing recommendations
- **Autonomous system prompt generation** - Dynamically generates its system prompt based on domain and context
- **Domain-specific guidance** - Provides expertise-specific guidance based on the specialty area
- **Follow-up support** - Allows up to 2 follow-up question rounds

```csharp
public class SpecialistAgent : IAgent
{
    public string Name => "SpecialistAgent";
    public string Description => "Expert consultant agent...";
    public bool RequiresCapableModel => false;
    public bool RequiresReviewModel => false;
    
    // Dynamically generated based on consultation context
    public string SystemPrompt { get; private set; }
    
    public string GenerateSystemPrompt() { /* ... */ }
    public List<ToolDefinition> GetToolDefinitions() => new(); // Empty - no tools!
}
```

### 4. ConsultSpecialistTool (`Abo.Core/Agents/ConsultSpecialistTool.cs`)

Tool enabling agents to request specialist consultation.

```csharp
public class ConsultSpecialistTool : IAboTool
{
    public string Name => "consult_specialist";
    
    // Parameters:
    // - taskDescription: Detailed task description
    // - contextSummary: Broader context summary
    // - specialistDomain: Optional domain (architecture, security, etc.)
}
```

### 5. ConsultationService (`Abo.Core/Services/IConsultationService.cs`)

Interface and implementation for running consultations.

```csharp
public interface IConsultationService
{
    Task<ConsultationResult> RunConsultationAsync(ConsultationRequest request);
}
```

### 6. Orchestrator Extensions (`Abo.Core/Core/Orchestrator.cs`)

Added `RunConsultationAsync` method to Orchestrator for handling specialist consultations.

## Communication Protocol

### Turn-Based Exchange

The specialist consultation uses a turn-based protocol with the following characteristics:

| Parameter | Value | Description |
|-----------|-------|-------------|
| Max Turns | 5 | Maximum number of LLM calls per consultation |
| Max Follow-up Rounds | 2 | Number of follow-up question rounds allowed |
| Session Prefix | `consult-` | Prefix for consultation session IDs |

### Message Format

Messages include:
- `role`: "system", "user", or "assistant"
- `content`: Message content
- `turnNumber`: Current turn (starts at 1)
- `conversationId`: Unique consultation ID

## Termination Mechanism

### Explicit Signals

The specialist can signal termination using these markers in the response:

| Signal | Meaning | Action |
|--------|---------|--------|
| `[CONSULTATION_COMPLETE]` | Specialist has provided complete answer | End consultation, return result |
| `[NEEDS_MORE_INFO]` | Specialist needs additional context | Allow follow-up or terminate at max rounds |

### Automatic Termination

Consultation ends automatically when:
- Max turns (5) reached
- Max follow-up rounds (2) reached
- API error occurs
- Exception thrown

### Sentinel Constants (`AgentSentinels.cs`)

```csharp
public static class AgentSentinels
{
    public const string ConsultationComplete = "[CONSULTATION_COMPLETE]";
    public const string NeedsMoreInfo = "[NEEDS_MORE_INFO]";
    public const string ConsultationTerminate = "[CONSULTATION_TERMINATE]";
    // + Existing sentinels for conclude_step, postpone_task
}
```

## LLM Selection Strategy

The specialist uses a different LLM than the caller for perspective diversity:

1. **Primary**: Uses `CapableModelName` if configured
2. **Fallback**: Uses default `ModelName`

The OpenRouterModelSelector ensures the CapableModel comes from a different vendor than the generic Model, providing optimal diversity.

## Divide-and-Conquer Trust Model

The system is designed for efficient delegation:

1. **Caller prepares comprehensive context** - Before calling the specialist, the caller prepares a good overview of the issue
2. **Specialist provides expert analysis** - Runs without tools, focused purely on recommendations
3. **Results trusted without re-validation** - Caller uses specialist results directly without checking everything back

This approach:
- Reduces lengthy conversations/threads
- Enables parallel problem solving
- Leverages specialized expertise efficiently

## Domain Support

The SpecialistAgent supports multiple domains with specialized guidance:

| Domain | Expertise Areas |
|--------|-----------------|
| architecture | Software patterns, system design, API design, data modeling |
| security | Auth patterns, encryption, vulnerabilities, compliance |
| performance | Optimization, caching, async patterns, scaling |
| database | Schema design, indexing, query optimization, migrations |
| frontend | UI/UX, state management, components, accessibility |
| backend | API design, business logic, error handling, microservices |
| devops | CI/CD, containers, IaC, monitoring, deployment |
| testing | Test strategy, TDD, mocking, quality metrics |
| general | General best practices (default fallback) |

## Usage Example

```csharp
// 1. Create consultation request
var request = new ConsultationRequest
{
    CallerAgentName = "ManagerAgent",
    SpecialistDomain = "architecture",
    TaskDescription = "Design a microservices architecture for a real-time chat application",
    ContextSummary = "We have 1M daily active users, need low latency, must handle 10K concurrent connections..."
};

// 2. Run consultation
var consultationService = new ConsultationService(orchestrator);
var result = await consultationService.RunConsultationAsync(request);

// 3. Use result
if (result.Success)
{
    var recommendations = result.SpecialistResponse;
    // Use specialist's recommendations directly (trust without re-validation)
}
```

## Integration Points

### With ManagerAgent
The `consult_specialist` tool can be added to ManagerAgent's toolset, allowing it to request specialist consultation when encountering complex tasks.

### With Orchestrator
The `RunConsultationAsync` method creates isolated sessions for each consultation, with separate tracking and consumption logging.

### With TrafficLoggerService
All consultation requests/responses are logged with the `CONSULTATION_REQUEST` and `CONSULTATION_RESPONSE` prefixes for debugging and analysis.

## Future Enhancements

1. **Domain-specific model selection** - Select models based on the specialist domain
2. **Conversation length threshold** - Automatically prompt to use specialist when conversation exceeds threshold
3. **Multi-specialist consultations** - Allow consulting multiple specialists for different aspects
4. **Persistent specialist memory** - Allow specialists to retain context across consultations
