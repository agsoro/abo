using System.Text.Json;
using Abo.Models;

namespace Abo.Tools;

public class GetRandomQuestionTool : IAboTool
{
    public string Name => "get_random_question";
    public string Description => "Gets a random quiz question from the datastore for a specific topic (or any topic if not specified). Use this to find a question to ask the user.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            topic = new
            {
                type = "string",
                description = "The topic to get a question for (e.g., nerdy, compliance, general). If omitted, a random topic is chosen."
            }
        },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson, options);
        string? topic = null;
        if (args != null && args.TryGetValue("topic", out var t))
        {
            topic = t;
        }

        topic = topic?.ToLowerInvariant();

        var questionsPath = "Data/Quiz/questions.json";
        if (!File.Exists(questionsPath))
        {
            return "No questions available in the datastore.";
        }

        var json = await File.ReadAllTextAsync(questionsPath);
        var questions = JsonSerializer.Deserialize<List<QuizQuestion>>(json, options) ?? new List<QuizQuestion>();

        if (questions.Count == 0)
        {
            return "No questions available in the datastore.";
        }

        if (!string.IsNullOrWhiteSpace(topic))
        {
            questions = questions.Where(q => q.Topic.Equals(topic, StringComparison.OrdinalIgnoreCase)).ToList();
            if (questions.Count == 0)
            {
                return $"No questions found for topic '{topic}'.";
            }
        }

        var random = new Random();
        var selectedQuestion = questions[random.Next(questions.Count)];

        var debugModeWarning = "\n[FOR AGENT: DO NOT REVEAL THE ANSWER IN YOUR CHAT MESSAGE EXCEPT WHEN THE USER ANSWERS.]";
        return JsonSerializer.Serialize(selectedQuestion, new JsonSerializerOptions { WriteIndented = true }) + debugModeWarning;
    }
}
