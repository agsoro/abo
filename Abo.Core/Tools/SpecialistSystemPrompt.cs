namespace Abo.Core;

/// <summary>
/// Generates system prompts for specialist consultation agents.
/// Part of the ConsultSpecialistTool implementation (Issue #407).
/// 
/// The specialist autonomously generates an appropriate system prompt based on the task context.
/// The personality is defined as "very special/nice specialist" - knowledgeable, friendly, clear, and practical.
/// No tools are available to the specialist (pure advisory role).
/// </summary>
public class SpecialistSystemPrompt
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Default configuration values.
    /// </summary>
    public static class Defaults
    {
        /// <summary>
        /// Maximum follow-up rounds allowed.
        /// </summary>
        public const int MaxFollowUps = 2;

        /// <summary>
        /// Maximum turns in a consultation.
        /// </summary>
        public const int MaxTurns = 5;

        /// <summary>
        /// Default domain when none specified.
        /// </summary>
        public const string DefaultDomain = "general";
    }

    public SpecialistSystemPrompt(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the configured maximum follow-up rounds.
    /// </summary>
    public int MaxFollowUps =>
        int.TryParse(_configuration["Consultation:MaxFollowUps"], out var max)
            ? max
            : Defaults.MaxFollowUps;

    /// <summary>
    /// Generates a system prompt for the specialist based on task context.
    /// </summary>
    /// <param name="taskDescription">The specific task to consult on.</param>
    /// <param name="contextSummary">Broader context for the consultation.</param>
    /// <param name="specialistDomain">Optional domain of expertise.</param>
    /// <returns>A complete system prompt for the specialist agent.</returns>
    public string GenerateSystemPrompt(string taskDescription, string contextSummary, string? specialistDomain = null)
    {
        var domain = specialistDomain ?? Defaults.DefaultDomain;
        var domainGuidance = GetDomainGuidance(domain);
        var maxFollowUps = MaxFollowUps;
        var maxTurns = Defaults.MaxTurns;

        return $@"You are an expert consultant specializing in {domain}.

## YOUR EXPERTISE
{domainGuidance}

## YOUR TASK
You have been consulted to provide expert advice on the following task:

**Task:** {taskDescription}

**Context:**
{contextSummary}

## CONSULTATION MESSAGE PROTOCOL (Issue #406)
You participate in a structured turn-based consultation with the following rules:

### Message Structure
- Each message should include the appropriate message type: 'task', 'response', 'clarification', or 'recommendation'
- Use markdown for structured content including headers, bullet points, and code blocks
- Include follow-up questions when needed, clearly marked

### Turn Limits
- Maximum total turns: {maxTurns} (including both parties)
- Maximum follow-up rounds from caller: {maxFollowUps}
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
|--------|-------------|
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
    }

    /// <summary>
    /// Generates a brief system prompt for quick consultations.
    /// </summary>
    /// <param name="domain">The domain of expertise.</param>
    /// <returns>A concise system prompt.</returns>
    public string GenerateBriefPrompt(string domain)
    {
        var guidance = GetDomainGuidance(domain);
        var domainGuidance = string.Join("\n", guidance.Split('\n').Take(3)); // First 3 lines only

        return $@"You are a {domain} expert consultant. 

Provide clear, actionable advice. Be concise and practical.

Expertise: {domainGuidance}

Use [CONSULTATION_COMPLETE] when done, [NEEDS_MORE_INFO] if you need clarification.";
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

            "code_review" => @"- Code quality assessment
- Best practices identification
- Potential bugs and issues
- Performance considerations
- Readability and maintainability",

            "debugging" => @"- Root cause analysis
- Common error patterns
- Logging and diagnostics
- Troubleshooting methodologies
- Fix verification strategies",

            "planning" => @"- Task breakdown
- Effort estimation
- Dependency identification
- Risk assessment
- Milestone planning",

            "refactoring" => @"- Code smell detection
- Pattern application
- Incremental refactoring
- Test coverage maintenance
- Performance impact analysis",

            _ => @"- General software engineering best practices
- Industry-standard patterns and principles
- Practical problem-solving approaches
- Code quality considerations
- Maintainability and scalability"
        };
    }

    /// <summary>
    /// Gets a list of all supported domains.
    /// </summary>
    public static IReadOnlyList<string> GetSupportedDomains()
    {
        return new[]
        {
            "architecture",
            "security",
            "performance",
            "database",
            "frontend",
            "backend",
            "devops",
            "testing",
            "implementation",
            "code_review",
            "debugging",
            "planning",
            "refactoring",
            "general"
        };
    }
}
