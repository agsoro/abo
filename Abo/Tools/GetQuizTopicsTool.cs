using System.Text.Json;

namespace Abo.Tools;

public class GetQuizTopicsTool : IAboTool
{
    public string Name => "get_quiz_topics";
    public string Description => "Lists all available quiz topics (pools) currently in the datastore.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var topicsDir = "Data/Quiz/topics";

        if (!Directory.Exists(topicsDir))
            return Task.FromResult("No topics currently available in the datastore.");

        var files = Directory.GetFiles(topicsDir, "*.json");
        if (files.Length == 0)
            return Task.FromResult("No topics currently available in the datastore.");

        var topics = files
            .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant())
            .OrderBy(t => t)
            .ToList();

        var topicsList = string.Join("\n- ", topics);
        return Task.FromResult($"Available Quiz Topics:\n- {topicsList}");
    }
}
