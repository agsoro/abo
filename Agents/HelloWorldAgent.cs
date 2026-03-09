using Abo.Contracts.OpenAI;
using Abo.Tools;

namespace Abo.Agents;

public class HelloWorldAgent : IAgent
{
    private readonly IAboTool _timeTool;
    private readonly IAboTool _multipleChoiceTool;

    public string Name => "HelloWorldAgent";
    public string Description => "A basic assistant that can tell the time (get_system_time) and ask about personal preferences like colors (ask_multiple_choice). Use this for general greetings and time queries.";

    public HelloWorldAgent(IEnumerable<IAboTool> tools)
    {
        _timeTool = tools.First(t => t.Name == "get_system_time");
        _multipleChoiceTool = tools.First(t => t.Name == "ask_multiple_choice");
    }

    public string SystemPrompt => 
        "You are a helpful assistant testing the ABO framework. " +
        "You MUST be conversational and polite. " +
        "When asked for the time, you MUST use the `get_system_time` tool and reply with the result in German. " +
        "When asked a personal preference question (like 'What is your favorite color?'), you MUST use the `ask_multiple_choice` tool to ask the user to pick from a list of 3 options instead of answering immediately.";

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
