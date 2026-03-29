using System.Text.Json;
using System.Text.Json.Serialization;
using Abo.Contracts.OpenAI;
using Abo.Core.Models;

namespace Abo.Agents;

/// <summary>
/// A consultant agent that provides expert consultation on complex tasks.
/// Runs without tools, on a different LLM than the caller, and generates its own system prompt.
/// This agent is used for the consultation feature - a quick advisory consultant.
/// </summary>
public class ConsultantAgent : IAgent
{
    private readonly string _specialistDomain;
    private readonly string _taskDescription;
    private readonly string _contextSummary;

    public string Name => "ConsultantAgent";
    public string Description => "Expert consultant agent that provides specialized knowledge and recommendations on complex tasks through a structured consultation protocol.";
    public bool RequiresCapableModel => false;
    public bool RequiresReviewModel => false;

    /// <summary>
    /// Creates a new ConsultantAgent with the specified context.
    /// </summary>
    /// <param name="specialistDomain">The domain of expertise (e.g., "architecture", "security").</param>
    /// <param name="taskDescription">The specific task to consult on.</param>
    /// <param name="contextSummary">Broader context for the consultation.</param>
    public ConsultantAgent(string? specialistDomain, string taskDescription, string contextSummary)
    {
        _specialistDomain = specialistDomain ?? "general";
        _taskDescription = taskDescription;
        _contextSummary = contextSummary;
    }

    /// <summary>
    /// System prompt is generated dynamically based on the consultation context.
    /// This is called by the Orchestrator during consultation setup.
    /// </summary>
    public string SystemPrompt { get; private set; } = string.Empty;

    /// <summary>
    /// Generates a comprehensive system prompt for the consultant based on the consultation context.
    /// Implements the Consultation Message Protocol defined in Issue #406.
    /// </summary>
    /// <returns>A detailed system prompt for the consultant agent.</returns>
    public string GenerateSystemPrompt()
    {
        var domainGuidance = GetDomainGuidance(_specialistDomain);

        SystemPrompt = $@"You are an expert consultant specializing in {_specialistDomain}.

## YOUR EXPERTISE
{domainGuidance}

## YOUR TASK
You have been consulted to provide expert advice on the following task:

**Task:** {_taskDescription}

**Context:**
{_contextSummary}

## CONSULTATION MESSAGE PROTOCOL (Issue #406)
You participate in a structured turn-based consultation with the following rules:

### Message Structure
- Each message should include the appropriate message type: 'task', 'response', 'clarification', or 'recommendation'
- Use markdown for structured content including headers, bullet points, and code blocks
- Include follow-up questions when needed, clearly marked

### Turn Limits
- Maximum total turns: 5 (including both parties)
- Maximum follow-up rounds from caller: 2
- The consultation should conclude efficiently - be thorough but focused

## YOUR APPROACH
1. Analyze the task carefully and provide concrete, actionable recommendations
2. Consider best practices, potential pitfalls, and trade-offs
3. Present your analysis in a clear, structured format using markdown
4. If you can provide a definitive solution, state it clearly with confidence
5. If you need more information, ask SPECIFIC questions - avoid generic inquiries
6. When providing recommendations, you may use structured format with priority levels (high/medium/low)

## OUTPUT FORMAT
When providing your analysis:
- Start with a brief summary of your understanding
- Use markdown headers for structure (## Analysis, ## Recommendations, ## Conclusion)
- Use bullet points for clarity
- Include code snippets if relevant
- End with your recommendation clearly stated

## TERMINATION SIGNALS (Critical)
You MUST use one of these signals to end your response when appropriate:

| Signal | When to Use |
|--------|------------|
| [CONSULTATION_COMPLETE] | You have provided a complete answer and the consultation should end |
| [CONCLUSION] | Final recommendation with explicit end marker |
| [NEEDS_MORE_INFO] | You require additional context before giving a final recommendation |

After your initial response:
- Wait for the caller's follow-up questions
- Answer follow-up questions concisely
- Conclude with [CONSULTATION_COMPLETE] or [CONCLUSION]

## IMPORTANT
- Do NOT call any tools - you are a pure advisory agent
- Be friendly, helpful, and professional
- Be direct and concise - avoid unnecessary pleasantries
- Support your recommendations with reasoning when appropriate

[END_SYSTEM_PROMPT]

Start your consultation response now. Be thorough but focused on the task at hand.";

        return SystemPrompt;
    }

    /// <summary>
    /// Provides domain-specific guidance based on the specialty area.
    /// </summary>
    private static string GetDomainGuidance(string domain)
    {
        return domain.ToLowerInvariant() switch
        {
            "architecture" => @"- Software architecture patterns (microservices, layered, event-driven)
- System design and scalability considerations
- API design and integration patterns
- Data modeling and storage strategies
- Technology selection rationale",

            "security" => @"- Authentication and authorization patterns
- Data protection and encryption
- Common vulnerabilities and mitigations
- Compliance considerations
- Secure coding practices",

            "performance" => @"- Optimization techniques and profiling
- Caching strategies
- Database query optimization
- Async patterns and concurrency
- Resource utilization and scaling",

            "database" => @"- Schema design and normalization
- Indexing strategies
- Query optimization
- Data migration patterns
- NoSQL vs SQL trade-offs",

            "frontend" => @"- UI/UX best practices
- State management patterns
- Component architecture
- Performance optimization
- Accessibility considerations",

            "backend" => @"- API design and REST principles
- Business logic organization
- Error handling patterns
- Authentication/authorization
- Microservices patterns",

            "devops" => @"- CI/CD pipeline design
- Container orchestration
- Infrastructure as code
- Monitoring and observability
- Deployment strategies",

            "testing" => @"- Test strategy and coverage
- Unit, integration, and E2E testing
- Test-driven development
- Mocking and stubbing patterns
- Quality metrics",

            "implementation" => @"- Code implementation patterns
- Design pattern application
- Error handling strategies
- Code review best practices
- Refactoring techniques",

            _ => @"- General software engineering best practices
- Industry-standard patterns and principles
- Practical problem-solving approaches
- Code quality considerations
- Maintainability and scalability"
        };
    }

    public List<ToolDefinition> GetToolDefinitions()
    {
        // Consultant agents have NO tools - they are pure advisory agents
        return new List<ToolDefinition>();
    }

    public Task<string> HandleToolCallAsync(ToolCall toolCall)
    {
        // This should never be called as ConsultantAgent has no tools
        return Task.FromResult("[ERROR] ConsultantAgent has no tools available.");
    }
}
