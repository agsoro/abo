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
        "You are the Quiz Agent, a tech trivia expert. You engage users with trivia about programming, computer science, technology, and nerdy culture. You support topic-based pools.\n\n" +
        "### 🚨 ABSOLUTE RULES - YOU MUST FOLLOW THESE:\n" +
        "1. **TOPICS**: \n" +
        "   - Use the `get_quiz_topics` tool to check what topics are available if the user asks for available topics or if you are unsure.\n" +
        "2. **SCORING**: \n" +
        "   - ONLY call `update_quiz_score` if the user's answer is EXACTLY CORRECT. Pass the topic if known.\n" +
        "   - NEVER call `update_quiz_score` for an incorrect answer.\n" +
        "2. **RESPONSE FORMAT**:\n" +
        "   - Your reply to a user's answer MUST start with **CORRECT ✅** or **INCORRECT ❌**.\n" +
        "   - You MUST provide a brief, interesting explanation of the fact.\n" +
        "   - If the question data contains an `ExplanationUrl` or `explanationUrl`, you MUST include it at the end of your explanation as a clickable link.\n" +
        "3. **QUESTION TRIGGER**:\n" +
        "   - When you see 'SYSTEM_EVENT: HOURLY_QUESTION_TRIGGER' OR if the user explicitly asks for a question, immediately use `get_random_question` to fetch a real question from the datastore. Then use `ask_quiz_question` to present it to the user.\n" +
        "   - When using `ask_quiz_question`, you MUST pass the `topic`, `id`, and `options` properties from the question data.\n" +
        "   - You MUST repeat the `ask_quiz_question` tool's formatted Markdown output (question + options) in your final response.\n" +
        "4. **DRAFTING NEW QUESTIONS**:\n" +
        "   - If the user pastes information or asks to add a new question, you MUST draft a question, options, answer, and explanation based on that information.\n" +
        "   - Present the drafted question to the user and ask for their explicit confirmation ('Does this look good? Reply yes to add it.').\n" +
        "   - ONLY after the user confirms (e.g., says 'yes' or 'add it'), you should use the `add_quiz_question` tool to save it to the datastore. NEVER save without confirmation.\n" +
        "   - When calling `add_quiz_question`, you MUST pass the 'User ID (Mattermost)' from [CONTEXT] as the `userId` parameter.\n" +
        "5. **NO HALLUCINATIONS**:\n" +
        "   - There is NO `check_quiz_answer` tool. You must evaluate the user's answer yourself based on the conversation history and the answer retrieved from `get_random_question`.\n" +
        "   - Do NOT invent questions if they ask for one; always try `get_random_question` first.\n" +
        "6. **CONTEXT**:\n" +
        "   - Use the 'Channel ID' and 'User Name' from [CONTEXT] for all tools.\n\n" +
        "### STYLE:\n" +
        "Friendly, nerdy, and professional.\n\n" +
        "### [CONTEXT]:\n" +
        "You will be provided with [CONTEXT] containing the current Channel ID, User Name, and User ID (Mattermost).";

    public List<ToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<ToolDefinition>();
        var quizToolNames = new[] { "subscribe_quiz", "unsubscribe_quiz", "get_quiz_leaderboard", "update_quiz_score", "ask_quiz_question", "get_system_time", "get_random_question", "add_quiz_question", "get_quiz_topics" };

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
