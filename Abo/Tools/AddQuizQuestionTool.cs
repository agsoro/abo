using System.Text.Json;
using Abo.Models;

namespace Abo.Tools;

public class AddQuizQuestionTool : IAboTool
{
    public string Name => "add_quiz_question";
    public string Description => "Adds a newly drafted and USER-CONFIRMED quiz question to the datastore. ONLY call this after you have proposed the question to the user and they have explicitly said 'yes'.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            topic = new { type = "string", description = "The topic pool for this question." },
            question = new { type = "string", description = "The question text." },
            options = new { type = "array", items = new { type = "string" }, description = "List of options." },
            answer = new { type = "string", description = "The exact correct answer text." },
            explanation = new { type = "string", description = "Explanation to provide after an answer is given." },
            explanationUrl = new { type = "string", description = "An optional URL pointing to more information or the source of the explanation." }
        },
        required = new[] { "topic", "question", "options", "answer", "explanation" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var argsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        DraftQuestionArgs? args;
        try 
        {
            args = JsonSerializer.Deserialize<DraftQuestionArgs>(argumentsJson, argsOptions);
        }
        catch 
        {
            return "Failed to parse arguments.";
        }

        if (args == null || string.IsNullOrWhiteSpace(args.Question) || args.Options == null || args.Options.Length == 0)
        {
            return "Invalid question data provided.";
        }

        var newQuestion = new QuizQuestion
        {
            Topic = args.Topic.ToLowerInvariant(),
            Question = args.Question,
            Explanation = args.Explanation,
            ExplanationUrl = args.ExplanationUrl
        };

        if (string.IsNullOrWhiteSpace(newQuestion.Topic)) newQuestion.Topic = "general";

        var letters = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
        var dictOptions = new Dictionary<string, string>();
        for (int i = 0; i < args.Options.Length; i++)
        {
            var letter = i < letters.Length ? letters[i] : (i + 1).ToString();
            dictOptions[letter] = args.Options[i];
            
            if (args.Options[i].Equals(args.Answer, StringComparison.OrdinalIgnoreCase) || args.Answer.Equals(letter, StringComparison.OrdinalIgnoreCase))
            {
                newQuestion.Answer = letter;
            }
        }
        
        if (string.IsNullOrEmpty(newQuestion.Answer))
        {
            newQuestion.Answer = "A"; // Fallback
        }

        newQuestion.Options = dictOptions;

        var dir = "Data/Quiz";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var questionsPath = Path.Combine(dir, "questions.json");
        List<QuizQuestion> questions = new();

        if (File.Exists(questionsPath))
        {
            var json = await File.ReadAllTextAsync(questionsPath);
            questions = JsonSerializer.Deserialize<List<QuizQuestion>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }

        // Avoid exact duplicates
        if (questions.Any(q => q.Question.Equals(newQuestion.Question, StringComparison.OrdinalIgnoreCase)))
        {
            return "This identical question already exists in the datastore.";
        }

        // Generate ID
        var prefix = newQuestion.Topic.ToUpperInvariant();
        if (prefix.Length < 4) prefix = prefix.PadRight(4, 'X');
        else prefix = prefix.Substring(0, 4);

        var topicCount = questions.Count(q => q.Topic.Equals(newQuestion.Topic, StringComparison.OrdinalIgnoreCase));
        newQuestion.Id = $"{prefix}{(topicCount + 1).ToString("D4")}";

        questions.Add(newQuestion);
        var updatedJson = JsonSerializer.Serialize(questions, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(questionsPath, updatedJson);

        // Update topics.json
        var topicsPath = Path.Combine(dir, "topics.json");
        List<string> topics = new();
        if (File.Exists(topicsPath))
        {
            var topicsJson = await File.ReadAllTextAsync(topicsPath);
            topics = JsonSerializer.Deserialize<List<string>>(topicsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        
        if (!topics.Contains(newQuestion.Topic, StringComparer.OrdinalIgnoreCase))
        {
            topics.Add(newQuestion.Topic);
            var updatedTopicsJson = JsonSerializer.Serialize(topics, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(topicsPath, updatedTopicsJson);
        }

        return $"Successfully added question '{newQuestion.Question}' to the '{newQuestion.Topic}' pool.";
    }

    private class DraftQuestionArgs
    {
        public string Topic { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string[] Options { get; set; } = Array.Empty<string>();
        public string Answer { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string? ExplanationUrl { get; set; }
    }
}
