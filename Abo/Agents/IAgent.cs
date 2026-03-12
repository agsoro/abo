using Abo.Contracts.OpenAI;

namespace Abo.Agents;

public interface IAgent
{
    string Name { get; }
    string Description { get; }
    string SystemPrompt { get; }
    bool RequiresCapableModel { get; }
    bool RequiresReviewModel { get; }
    List<ToolDefinition> GetToolDefinitions();
    Task<string> HandleToolCallAsync(ToolCall toolCall);
}
