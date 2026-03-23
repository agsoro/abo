using System.Text.Json;
using System.Text.RegularExpressions;
using Abo.Models;
using Abo.Integrations.XpectoLive;
using Abo.Integrations.XpectoLive.Models;

namespace Abo.Tools;

public class GetRandomQuestionTool : IAboTool
{
    private readonly IXpectoLiveWikiClient _wikiClient;
    private const string SpaceId = "abo";

    public GetRandomQuestionTool(IXpectoLiveWikiClient wikiClient)
    {
        _wikiClient = wikiClient;
    }

    public string Name => "get_random_question";
    public string Description => "Gets a random quiz question from the Wiki for a specific topic (or any topic if not specified). Use this to find a question to ask the user.";

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

        try
        {
            var spaceInfo = await _wikiClient.GetSpaceInfoAsync(SpaceId);
            var rootPage = spaceInfo.FirstOrDefault(p => "quizcontent".Equals(p.PageTitle, StringComparison.OrdinalIgnoreCase));
            if (rootPage?.PageID == null) return "No questions available in the Wiki.";

            var aboSpace = await _wikiClient.GetSpaceAsync(SpaceId);
            if (aboSpace.StartPage == null) return "No questions available in the Wiki.";

            var availableTopics = GetTopicPages(aboSpace.StartPage, rootPage.PageID);
            if (!availableTopics.Any()) return "No questions available in the Wiki.";

            // Filter by topic if specified
            List<clWikiTree> topicsToSearch;
            if (!string.IsNullOrWhiteSpace(topic))
            {
                var specificTopic = availableTopics.FirstOrDefault(x => x.Title?.Equals(topic, StringComparison.OrdinalIgnoreCase) == true);
                if (specificTopic == null) return $"No questions found for topic '{topic}'.";
                topicsToSearch = new List<clWikiTree> { specificTopic };
            }
            else
            {
                topicsToSearch = availableTopics;
            }

            var allQuestions = new List<QuizQuestion>();
            foreach (var tNode in topicsToSearch)
            {
                if (string.IsNullOrEmpty(tNode.Id) || string.IsNullOrEmpty(tNode.Title)) continue;
                
                var pageInfo = await _wikiClient.GetPageAsync(SpaceId, tNode.Id);
                if (!string.IsNullOrEmpty(pageInfo.Content))
                {
                    var parsed = ParseHtmlToQuestions(pageInfo.Content, tNode.Title.ToLowerInvariant());
                    allQuestions.AddRange(parsed);
                }
            }

            if (allQuestions.Count == 0)
                return string.IsNullOrWhiteSpace(topic)
                    ? "No questions available in the Wiki."
                    : $"No questions found for topic '{topic}'.";

            var selected = allQuestions[new Random().Next(allQuestions.Count)];
            var debugWarning = "\n[FOR AGENT: DO NOT REVEAL THE ANSWER IN YOUR CHAT MESSAGE EXCEPT WHEN THE USER ANSWERS.]";
            return JsonSerializer.Serialize(selected, new JsonSerializerOptions { WriteIndented = true }) + debugWarning;
        }
        catch (Exception ex)
        {
            return $"Error fetching random question: {ex.Message}";
        }
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

    private List<QuizQuestion> ParseHtmlToQuestions(string html, string topicName)
    {
        var questions = new List<QuizQuestion>();
        
        // This regex breaks the HTML into chunks starting with <h2>ID: 
        var chunks = html.Split(new[] { "<h2>ID: " }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var chunk in chunks)
        {
            if (!chunk.Contains("</h2>")) continue;

            var q = new QuizQuestion { Topic = topicName };

            // Extract ID
            var idMatch = Regex.Match("<h2>ID: " + chunk, @"<h2>ID:\s*(.*?)\s*</h2>", RegexOptions.IgnoreCase);
            if (idMatch.Success) q.Id = idMatch.Groups[1].Value.Trim();

            // Extract Question
            var qMatch = Regex.Match(chunk, @"<p><strong>Question:</strong>\s*(.*?)\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (qMatch.Success) q.Question = System.Net.WebUtility.HtmlDecode(qMatch.Groups[1].Value.Replace("<br/>", "\n").Trim());

            // Extract Answer
            var aMatch = Regex.Match(chunk, @"<p><strong>Answer:</strong>\s*(.*?)\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (aMatch.Success) q.Answer = System.Net.WebUtility.HtmlDecode(aMatch.Groups[1].Value.Replace("<br/>", "\n").Trim());

            // Extract Explanation
            var eMatch = Regex.Match(chunk, @"<p><strong>Explanation:</strong>\s*(.*?)\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (eMatch.Success) q.Explanation = System.Net.WebUtility.HtmlDecode(eMatch.Groups[1].Value.Replace("<br/>", "\n").Trim());

            // Extract ExplanationUrl
            var euMatch = Regex.Match(chunk, @"<p><strong>ExplanationUrl:</strong>.*?href=""([^""]+)"".*?</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (euMatch.Success) q.ExplanationUrl = euMatch.Groups[1].Value.Trim();

            // Extract Options
            var optsMatch = Regex.Match(chunk, @"<ul>(.*?)</ul>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (optsMatch.Success)
            {
                q.Options = new Dictionary<string, string>();
                var listItems = Regex.Matches(optsMatch.Groups[1].Value, @"<li><strong>(.*?):</strong>\s*(.*?)\s*</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                foreach (Match item in listItems)
                {
                    var optKey = item.Groups[1].Value.Trim();
                    var optVal = System.Net.WebUtility.HtmlDecode(item.Groups[2].Value.Replace("<br/>", "\n").Trim());
                    q.Options[optKey] = optVal;
                }
            }

            if (!string.IsNullOrEmpty(q.Id) && !string.IsNullOrEmpty(q.Question))
            {
                questions.Add(q);
            }
        }

        return questions;
    }
}
