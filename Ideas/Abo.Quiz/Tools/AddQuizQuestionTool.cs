using System.Text.Json;
using System.Text.RegularExpressions;
using Abo.Models;
using Abo.Services;
using Abo.Integrations.XpectoLive;
using Abo.Integrations.XpectoLive.Models;

namespace Abo.Tools;

public class AddQuizQuestionTool : IAboTool
{
    private readonly UserService _userService;
    private readonly IXpectoLiveWikiClient _wikiClient;
    private const string SpaceId = "abo";

    public AddQuizQuestionTool(UserService userService, IXpectoLiveWikiClient wikiClient)
    {
        _userService = userService;
        _wikiClient = wikiClient;
    }

    public string Name => "add_quiz_question";
    public string Description => "Adds a newly drafted and USER-CONFIRMED quiz question to the Wiki. ONLY call this after you have proposed the question to the user and they have explicitly said 'yes'. Requires the user to have the 'quiz-admin' role.";

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

        try
        {
            var spaceInfo = await _wikiClient.GetSpaceInfoAsync(SpaceId);
            var rootPage = spaceInfo.FirstOrDefault(p => "quizcontent".Equals(p.PageTitle, StringComparison.OrdinalIgnoreCase));
            if (rootPage?.PageID == null) return "Wiki structure not initialized. Root page 'quizcontent' missing.";

            var aboSpace = await _wikiClient.GetSpaceAsync(SpaceId);
            if (aboSpace.StartPage == null) return "Wiki structure not initialized.";

            var availableTopics = GetTopicPages(aboSpace.StartPage, rootPage.PageID);
            var targetTopicNode = availableTopics.FirstOrDefault(x => x.Title?.Equals(topic, StringComparison.OrdinalIgnoreCase) == true);
            
            if (targetTopicNode == null || string.IsNullOrEmpty(targetTopicNode.Id))
            {
                return $"Error: The topic '{topic}' does not exist in the Wiki. Add it first before adding questions.";
            }

            var pageInfo = await _wikiClient.GetPageAsync(SpaceId, targetTopicNode.Id);
            var existingHtml = pageInfo.Content ?? string.Empty;

            // Duplicate check
            var decodedQuestion = System.Net.WebUtility.HtmlEncode(args.Question).Replace("\n", "<br/>");
            if (existingHtml.Contains(decodedQuestion, StringComparison.OrdinalIgnoreCase))
                return "This identical question already exists in the datastore.";

            // ID Generation
            var existingIds = new List<string>();
            var idMatches = Regex.Matches(existingHtml, @"<h2>ID:\s*(.*?)\s*</h2>", RegexOptions.IgnoreCase);
            
            foreach (Match match in idMatches)
            {
                existingIds.Add(match.Groups[1].Value.Trim());
            }

            var prefix = topic.ToUpperInvariant();
            prefix = prefix.Length < 4 ? prefix.PadRight(4, 'X') : prefix[..4];

            var maxNum = 0;
            foreach (var id in existingIds)
            {
                if (id.StartsWith(prefix) && int.TryParse(id.Substring(prefix.Length), out var n))
                {
                    if (n > maxNum) maxNum = n;
                }
            }

            var newId = $"{prefix}{(maxNum + 1).ToString("D4")}";

            // Format Options
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

            // Construct new HTML block
            var html = new System.Text.StringBuilder();
            html.AppendLine($"<h2>ID: {newId}</h2>");
            html.AppendLine($"<p><strong>Question:</strong> {ConvertNewlines(args.Question)}</p>");
            
            html.AppendLine("<ul>");
            foreach (var opt in dictOptions)
            {
                html.AppendLine($"<li><strong>{opt.Key}:</strong> {ConvertNewlines(opt.Value)}</li>");
            }
            html.AppendLine("</ul>");

            html.AppendLine($"<p><strong>Answer:</strong> {answerKey}</p>");
            html.AppendLine($"<p><strong>Explanation:</strong> {ConvertNewlines(args.Explanation)}</p>");
            
            if (!string.IsNullOrWhiteSpace(args.ExplanationUrl))
            {
                html.AppendLine($"<p><strong>ExplanationUrl:</strong> <a href=\"{args.ExplanationUrl}\">{args.ExplanationUrl}</a></p>");
            }
            html.AppendLine("<hr/>");

            var newHtml = existingHtml + "\n" + html.ToString();

            // Update page
            await _wikiClient.UpdatePageDraftAsync(SpaceId, targetTopicNode.Id, new ContentUpdate
            {
                VersionComment = "Added question " + newId,
                Content = newHtml
            });

            await _wikiClient.PublishPageDraftAsync(SpaceId, targetTopicNode.Id);

            return $"Successfully added question '{args.Question}' as **{newId}** to the '{topic}' pool in the Wiki.";
        }
        catch (Exception ex)
        {
            return $"Error updating wiki: {ex.Message}";
        }
    }

    private string ConvertNewlines(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var encoded = System.Net.WebUtility.HtmlEncode(input);
        return encoded.Replace("\n", "<br/>");
    }

    private List<clWikiTree> GetTopicPages(clWikiTree node, string rootId)
    {
        var result = new List<clWikiTree>();

        if (node.Id == rootId)
        {
            if (node.Childs != null)
            {
                result.AddRange(node.Childs);
            }
            return result;
        }

        if (node.Childs != null)
        {
            foreach (var child in node.Childs)
            {
                result.AddRange(GetTopicPages(child, rootId));
            }
        }

        return result;
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
