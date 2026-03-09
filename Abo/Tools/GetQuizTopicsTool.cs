using System.Text.Json;
using Abo.Models;

namespace Abo.Tools;

public class GetQuizTopicsTool : IAboTool
{
    public string Name => "get_quiz_topics";
    public string Description => "Lists all available quiz topics (pools) currently in the datastore.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { }, // No parameters needed
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var topicsPath = "Data/Quiz/topics.json";
        var questionsPath = "Data/Quiz/questions.json";
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<string> topics = new();

        try
        {
            if (File.Exists(topicsPath))
            {
                var json = await File.ReadAllTextAsync(topicsPath);
                topics = JsonSerializer.Deserialize<List<string>>(json, options) ?? new List<string>();
            }
            
            // Auto-populate from questions if topics is empty or doesn't exist yet
            if (topics.Count == 0 && File.Exists(questionsPath))
            {
                var qJson = await File.ReadAllTextAsync(questionsPath);
                var questions = JsonSerializer.Deserialize<List<QuizQuestion>>(qJson, options) ?? new List<QuizQuestion>();
                topics = questions.Select(q => q.Topic.ToLowerInvariant()).Distinct().Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                
                // Save the populated topics list
                var dir = "Data/Quiz";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var updatedJson = JsonSerializer.Serialize(topics, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(topicsPath, updatedJson);
            }
        }
        catch (Exception ex)
        {
            return $"Error retrieving topics: {ex.Message}";
        }

        if (topics.Count == 0)
        {
            return "No topics currently available in the datastore.";
        }

        var topicsList = string.Join("\n- ", topics);
        return $"Available Quiz Topics:\n- {topicsList}";
    }
}
