using Abo.Contracts.OpenAI;
using Abo.Tools;

namespace Abo.Agents;

public class HelloWorldAgent : IAgent
{
    private readonly IAboTool _timeTool;
    private readonly IAboTool _multipleChoiceTool;

    public string Name => "HelloWorldAgent";
    public string Description => "A basic assistant that can tell the time (get_system_time) and ask about personal preferences like colors (ask_multiple_choice). Use this for general greetings and time queries.";
    public bool RequiresCapableModel => false;
    public bool RequiresReviewModel => false;

    public HelloWorldAgent(IEnumerable<IAboTool> tools)
    {
        _timeTool = tools.First(t => t.Name == "get_system_time");
        _multipleChoiceTool = tools.First(t => t.Name == "ask_multiple_choice");
    }

    public string SystemPrompt =>
        "When asked the time, use `get_system_time`. " +
        "For greetings, be friendly. " +
        "IMPORTANT: You are NOT the Quiz Agent. If the user is answering a quiz or asking about the leaderboard, the Supervisor should have routed them elsewhere, but if you see it, do NOT attempt to use quiz tools or hallucinate `check_quiz_answer`.";

    public List<ToolDefinition> GetToolDefinitions()
    {
        return new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = _timeTool.Name,
                    Description = _timeTool.Description,
                    Parameters = _timeTool.ParametersSchema
                }
            },
            new ToolDefinition
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = _multipleChoiceTool.Name,
                    Description = _multipleChoiceTool.Description,
                    Parameters = _multipleChoiceTool.ParametersSchema
                }
            }
        };
    }

    public async Task<string> HandleToolCallAsync(Abo.Contracts.OpenAI.ToolCall toolCall)
    {
        if (toolCall.Function?.Name == _timeTool.Name)
        {
            return await _timeTool.ExecuteAsync(toolCall.Function.Arguments ?? "{}");
        }

        if (toolCall.Function?.Name == _multipleChoiceTool.Name)
        {
            return await _multipleChoiceTool.ExecuteAsync(toolCall.Function.Arguments ?? "{}");
        }

        return $"Error: Unknown tool '{toolCall.Function?.Name}'";
    }
}
