using System.Text.Json;
using System.Text.Json.Nodes;

namespace Abo.Tools;

public class AskMultipleChoiceTool : IAboTool
{
    public string Name => "ask_multiple_choice";
    public string Description => "Asks the user a multiple-choice question. Always use this tool when you need the user to select from a predefined list of options.";
    
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            question = new
            {
                type = "string",
                description = "The main question to ask the user"
            },
            options = new
            {
                type = "array",
                description = "A list of 2 to 10 possible text options the user can select from",
                items = new { type = "string" }
            }
        },
        required = new[] { "question", "options" },
        additionalProperties = false
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<AskMultipleChoiceArgs>(argumentsJson);
        if (args == null || string.IsNullOrWhiteSpace(args.Question) || args.Options == null || args.Options.Length == 0)
        {
            return Task.FromResult("Error: Invalid arguments provided to tool.");
        }

        // We do not actually await a real user input here (the Orchestrator loop manages that).
        // This tool simply formats the LLM's intent into a clean Markdown string that the Orchestrator will yield to the chat.
        // Once the Orchestrator prints this, it stops. When the user eventually types a number in the chat, 
        // the LLM will see this formatted string in history plus the user's "3", and naturally understand they picked option 3.
        
        var formattedOutput = $"**{args.Question}**\n\n";
        for (int i = 0; i < args.Options.Length; i++)
        {
            formattedOutput += $"**{i + 1}.** {args.Options[i]}\n";
        }
        formattedOutput += "\n*(Please reply with the number or text of your choice)*";

        return Task.FromResult(formattedOutput);
    }

    private class AskMultipleChoiceArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("options")]
        public string[] Options { get; set; } = Array.Empty<string>();
    }
}
