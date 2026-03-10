using System.Text.Json;
using Abo.Models;
using Abo.Services;

namespace Abo.Tools;

public class AddQuizQuestionTool : IAboTool
{
    private readonly UserService _userService;

    public AddQuizQuestionTool(UserService userService)
    {
        _userService = userService;
    }

    public string Name => "add_quiz_question";
    public string Description => "Adds a newly drafted and USER-CONFIRMED quiz question to the datastore. ONLY call this after you have proposed the question to the user and they have explicitly said 'yes'. Requires the user to have the 'quiz-admin' role.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            userId = new { type = "string", description = "The Mattermost User ID of the requesting user (for authorization)." },
            topic = new { type = "string", description = "The topic pool for this question." },
            question = new { type = "string", description = "The question text." },
            options = new { type = "array", items = new { type = "string" }, description = "List of options." },
            answer = new { type = "string", description = "The exact correct answer text." },
            explanation = new { type = "string", description = "Explanation to provide after an answer is given." },
            explanationUrl = new { type = "string", description = "An optional URL pointing to more information or the source of the explanation." }
        },
        required = new[] { "userId", "topic", "question", "options", "answer", "explanation" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        DraftQuestionArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<DraftQuestionArgs>(argumentsJson, jsOptions);
        }
        catch
        {
            return "Failed to parse arguments.";
        }

        if (args == null || string.IsNullOrWhiteSpace(args.Question) || args.Options == null || args.Options.Length == 0)
            return "Invalid question data provided.";

        // Authorization check
        if (string.IsNullOrWhiteSpace(args.UserId) || !_userService.HasRole(args.UserId, "quiz-admin"))
            return "❌ Zugriff verweigert: Du benötigst die Rolle **quiz-admin**, um Fragen hinzuzufügen.";

        var topic = args.Topic.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(topic)) topic = "general";

        // Build options dict (A, B, C ...)
        var letters = new[] { "A", "B", "C", "D", "E", "F", "G", "H" };
        var dictOptions = new Dictionary<string, string>();
        string answerKey = "A";

        for (int i = 0; i < args.Options.Length; i++)
        {
            var letter = i < letters.Length ? letters[i] : (i + 1).ToString();
            dictOptions[letter] = args.Options[i];

            if (args.Options[i].Equals(args.Answer, StringComparison.OrdinalIgnoreCase)
                || args.Answer.Equals(letter, StringComparison.OrdinalIgnoreCase))
            {
                answerKey = letter;
            }
        }

        // Load existing topic file
        var topicsDir = "Data/Quiz/topics";
        if (!Directory.Exists(topicsDir)) Directory.CreateDirectory(topicsDir);

        var topicFile = Path.Combine(topicsDir, topic + ".json");
        var existing = new Dictionary<string, QuizQuestion>();

        if (File.Exists(topicFile))
        {
            var json = await File.ReadAllTextAsync(topicFile);
            existing = JsonSerializer.Deserialize<Dictionary<string, QuizQuestion>>(json, jsOptions) ?? new();
        }

        // Duplicate check
        if (existing.Values.Any(q => q.Question.Equals(args.Question, StringComparison.OrdinalIgnoreCase)))
            return "This identical question already exists in the datastore.";

        // Generate sequential ID
        var prefix = topic.ToUpperInvariant();
        prefix = prefix.Length < 4 ? prefix.PadRight(4, 'X') : prefix[..4];
        var newId = $"{prefix}{(existing.Count + 1).ToString("D4")}";

        var newQuestion = new QuizQuestion
        {
            Id = newId,
            Topic = topic,
            Question = args.Question,
            Options = dictOptions,
            Answer = answerKey,
            Explanation = args.Explanation,
            ExplanationUrl = args.ExplanationUrl
        };

        existing[newId] = newQuestion;
        var updatedJson = JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(topicFile, updatedJson);

        return $"Successfully added question '{newQuestion.Question}' as **{newId}** to the '{topic}' pool.";
    }

    private class DraftQuestionArgs
    {
        public string UserId { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string[] Options { get; set; } = Array.Empty<string>();
        public string Answer { get; set; } = string.Empty;
        public string Explanation { get; set; } = string.Empty;
        public string? ExplanationUrl { get; set; }
    }
}
