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
            topic = t?.ToLowerInvariant();

        var topicsDir = "Data/Quiz/topics";
        if (!Directory.Exists(topicsDir))
            return "No questions available in the datastore.";

        // Determine which topic files to search
        IEnumerable<string> files;
        if (!string.IsNullOrWhiteSpace(topic))
        {
            var specificFile = Path.Combine(topicsDir, topic + ".json");
            if (!File.Exists(specificFile))
                return $"No questions found for topic '{topic}'.";
            files = new[] { specificFile };
        }
        else
        {
            files = Directory.GetFiles(topicsDir, "*.json");
        }

        // Gather all questions across selected files
        var all = new List<QuizQuestion>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file);
            var dict = JsonSerializer.Deserialize<Dictionary<string, QuizQuestion>>(json, options) ?? new();
            all.AddRange(dict.Values);
        }

        if (all.Count == 0)
            return string.IsNullOrWhiteSpace(topic)
                ? "No questions available in the datastore."
                : $"No questions found for topic '{topic}'.";

        var selected = all[new Random().Next(all.Count)];
        var debugWarning = "\n[FOR AGENT: DO NOT REVEAL THE ANSWER IN YOUR CHAT MESSAGE EXCEPT WHEN THE USER ANSWERS.]";
        return JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true }) + debugWarning;
    }
}
