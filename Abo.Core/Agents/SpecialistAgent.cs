using System.Text.Json;
using Abo.Contracts.OpenAI;

namespace Abo.Agents;

/// <summary>
/// A specialist agent that provides expert consultation on complex tasks.
/// Runs without tools, on a different LLM than the caller, and generates its own system prompt.
/// </summary>
public class SpecialistAgent : IAgent
{
    private readonly string _specialistDomain;
    private readonly string _taskDescription;
    private readonly string _contextSummary;

    public string Name => "SpecialistAgent";
    public string Description => "Expert consultant agent that provides specialized knowledge and recommendations on complex tasks.";
    public bool RequiresCapableModel => false;
    public bool RequiresReviewModel => false;

    /// <summary>
    /// Creates a new SpecialistAgent with the specified context.
    /// </summary>
    /// <param name="specialistDomain">The domain of expertise (e.g., "architecture", "security").</param>
    /// <param name="taskDescription">The specific task to consult on.</param>
    /// <param name="contextSummary">Broader context for the consultation.</param>
    public SpecialistAgent(string? specialistDomain, string taskDescription, string contextSummary)
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
    /// Generates a comprehensive system prompt for the specialist based on the consultation context.
    /// </summary>
    /// <returns>A detailed system prompt for the specialist agent.</returns>
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

## YOUR APPROACH
1. Analyze the task carefully and provide concrete, actionable recommendations
2. Consider best practices, potential pitfalls, and trade-offs
3. Present your analysis in a clear, structured format using markdown
4. If you can provide a definitive solution, state it clearly with confidence
5. If you need more information, ask SPECIFIC questions - avoid generic inquiries

## COMMUNICATION RULES
- Be friendly, helpful, and professional
- Provide expert-level insights
- Be direct and concise - avoid unnecessary pleasantries
- Support your recommendations with reasoning when appropriate
- You may allow up to 2 follow-up questions for clarification

## TERMINATION
- End your response with [CONSULTATION_COMPLETE] when you have provided a complete answer
- Use [NEEDS_MORE_INFO] if you require additional context to give a proper recommendation
- After answering, await follow-up questions - the consultation ends after you answer them or choose to conclude
- Do NOT call any tools - you are a pure advisory agent

## OUTPUT FORMAT
When providing your analysis:
- Use markdown headers for structure
- Use bullet points for clarity
- Include code snippets if relevant
- End with your recommendation clearly stated

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

            _ => @"- General software engineering best practices
- Industry-standard patterns and principles
- Practical problem-solving approaches
- Code quality considerations
- Maintainability and scalability"
        };
    }

    public List<ToolDefinition> GetToolDefinitions()
    {
        // Specialist agents have NO tools - they are pure advisory agents
        return new List<ToolDefinition>();
    }

    public Task<string> HandleToolCallAsync(ToolCall toolCall)
    {
        // This should never be called as SpecialistAgent has no tools
        return Task.FromResult("[ERROR] SpecialistAgent has no tools available.");
    }
}
