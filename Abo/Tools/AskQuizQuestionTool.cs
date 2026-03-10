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
            id = new
            {
                type = "string",
                description = "The unique question ID (e.g. COMP0001). Pass through from get_random_question."
            },
            topic = new
            {
                type = "string",
                description = "The topic pool this question belongs to (e.g., nerdy, compliance, general). Defaults to general if not specified."
            },
            question = new
            {
                type = "string",
                description = "The main trivia question to ask the user"
            },
            options = new
            {
                type = "object",
                description = "A mapping of option identifiers (e.g. A, B, C) to the option text.",
                additionalProperties = new { type = "string" }
            }
        },
        required = new[] { "question", "options" },
        additionalProperties = false
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var args = JsonSerializer.Deserialize<AskQuizQuestionArgs>(argumentsJson);
        if (args == null || string.IsNullOrWhiteSpace(args.Question) || args.Options == null || args.Options.Count == 0)
        {
            return Task.FromResult("Error: Invalid arguments provided to tool.");
        }

        var topic = string.IsNullOrWhiteSpace(args.Topic) ? "general" : args.Topic;
        var formattedOutput = $"**[Topic: {topic}] [ID: {args.Id}]**\n**{args.Question}**\n\n";
        foreach (var opt in args.Options)
        {
            formattedOutput += $"**{opt.Key}.** {opt.Value}\n";
        }
        formattedOutput += "\n*(Bitte antworte mit dem Buchstaben deiner Wahl)*";

        return Task.FromResult(formattedOutput);
    }

    private class AskQuizQuestionArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("topic")]
        public string? Topic { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("options")]
        public Dictionary<string, string> Options { get; set; } = new();
    }
}
