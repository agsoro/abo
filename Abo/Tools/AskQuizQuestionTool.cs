using System.Text.Json;

namespace Abo.Tools;

public class AskQuizQuestionTool : IAboTool
{
    public string Name => "ask_quiz_question";
    public string Description => "Asks the user a quiz-related multiple-choice question. Use this for all trivia and quiz questions.";
    
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            question = new
            {
                type = "string",
                description = "The main trivia question to ask the user"
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
        var args = JsonSerializer.Deserialize<AskQuizQuestionArgs>(argumentsJson);
        if (args == null || string.IsNullOrWhiteSpace(args.Question) || args.Options == null || args.Options.Length == 0)
        {
            return Task.FromResult("Error: Invalid arguments provided to tool.");
        }

        var formattedOutput = $"**{args.Question}**\n\n";
        for (int i = 0; i < args.Options.Length; i++)
        {
            formattedOutput += $"**{i + 1}.** {args.Options[i]}\n";
        }
        formattedOutput += "\n*(Please reply with the number or text of your choice)*";

        return Task.FromResult(formattedOutput);
    }

    private class AskQuizQuestionArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("options")]
        public string[] Options { get; set; } = Array.Empty<string>();
    }
}
