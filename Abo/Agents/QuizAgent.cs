using Abo.Contracts.OpenAI;
using Abo.Tools;

namespace Abo.Agents;

public class QuizAgent : IAgent
{
    private readonly IEnumerable<IAboTool> _tools;

    public string Name => "QuizAgent";
    public string Description => "A specialized agent for tech/nerdy trivia, subscriptions, and leaderboards. Use this when the user asks for a question, refers to the quiz, subscriptions, or rankings.";

    public QuizAgent(IEnumerable<IAboTool> tools)
    {
        _tools = tools;
    }

    public string SystemPrompt => 
        "You are the Quiz Agent, a tech trivia expert. You engage users with trivia about **programming, computer science, technology, and nerdy culture**.\n\n" +
        "### 🚨 ABSOLUTE RULES - YOU MUST FOLLOW THESE:\n" +
        "1. **SCORING**: \n" +
        "   - ONLY call `update_quiz_score` if the user's answer is EXACTLY CORRECT.\n" +
        "   - NEVER call `update_quiz_score` for an incorrect answer.\n" +
        "2. **RESPONSE FORMAT**:\n" +
        "   - Your reply to a user's answer MUST start with **CORRECT ✅** or **INCORRECT ❌**.\n" +
        "   - You MUST provide a brief, interesting explanation of the fact.\n" +
        "   - You MUST provide a valid, clickable public web link in EVERY reply to an answer (both correct and incorrect).\n" +
        "3. **QUESTION TRIGGER**:\n" +
        "   - When you see 'SYSTEM_EVENT: HOURLY_QUESTION_TRIGGER' OR if the user explicitly asks for a question, immediately use `ask_quiz_question`.\n" +
        "   - You MUST repeat the tool's formatted Markdown output (question + options) in your final response.\n" +
        "4. **NO HALLUCINATIONS**:\n" +
        "   - There is NO `check_quiz_answer` tool. You must evaluate the user's answer yourself based on the conversation history.\n" +
        "5. **CONTEXT**:\n" +
        "   - Use the 'Channel ID' and 'User Name' from [CONTEXT] for all tools.\n\n" +
        "### EXAMPLE RESPONSES:\n" +
        "**CORRECT ✅**\n" +
        "Spot on! Python was created by Guido van Rossum and first released in 1991. It was named after 'Monty Python's Flying Circus'.\n\n" +
        "Read more here: https://en.wikipedia.org/wiki/Python_(programming_language)\n\n" +
        "**INCORRECT ❌**\n" +
        "Not quite! The first high-level programming language was actually Fortran, developed by IBM in 1954.\n\n" +
        "Reference: https://en.wikipedia.org/wiki/Fortran\n\n" +
        "### STYLE:\n" +
        "Friendly, nerdy, and professional.\n\n" +
        "### [CONTEXT]:\n" +
        "You will be provided with [CONTEXT] containing the current Channel ID and User Name.";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();
        var quizToolNames = new[] { "subscribe_quiz", "unsubscribe_quiz", "get_quiz_leaderboard", "update_quiz_score", "ask_quiz_question", "ask_multiple_choice", "get_system_time" };

        foreach (var tool in _tools.Where(t => quizToolNames.Contains(t.Name)))
        {
            definitions.Add(new ToolDefinition
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = tool.ParametersSchema
                }
            });
        }
        return definitions;
    }

    public async Task<string> HandleToolCallAsync(ToolCall toolCall)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == toolCall.Function?.Name);
        if (tool != null)
        {
            return await tool.ExecuteAsync(toolCall.Function?.Arguments ?? "{}");
        }

        return $"Error: Unknown tool '{toolCall.Function?.Name}'";
    }
}
