using System.Text.Json;
using Abo.Integrations.XpectoLive;

namespace Abo.Tools;

public class GetQuizTopicsTool : IAboTool
{
    private readonly IXpectoLiveWikiClient _wikiClient;
    private const string SpaceId = "abo";

    public GetQuizTopicsTool(IXpectoLiveWikiClient wikiClient)
    {
        _wikiClient = wikiClient;
    }

    public string Name => "get_quiz_topics";
    public string Description => "Lists all available quiz topics (pools) from the Wiki.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            var spaceInfo = await _wikiClient.GetSpaceInfoAsync(SpaceId);
            
            // Find root page 'quizcontent'
            var rootPage = spaceInfo.FirstOrDefault(p => "quizcontent".Equals(p.PageTitle, StringComparison.OrdinalIgnoreCase));
            if (rootPage?.PageID == null)
            {
                return "No topics currently available in the Wiki.";
            }

            // Get the full page data for the start page to traverse the tree, or use clWikiTree
            var aboSpace = await _wikiClient.GetSpaceAsync(SpaceId);
            if (aboSpace.StartPage == null) return "No topics currently available in the Wiki.";

            var topics = FindTopicsUnderRoot(aboSpace.StartPage, rootPage.PageID);

            if (!topics.Any())
                return "No topics currently available in the Wiki.";

            var topicsList = string.Join("\n- ", topics.OrderBy(t => t));
            return $"Available Quiz Topics:\n- {topicsList}";
        }
        catch (Exception ex)
        {
            return $"Error fetching topics: {ex.Message}";
        }
    }

    private List<string> FindTopicsUnderRoot(Integrations.XpectoLive.Models.clWikiTree node, string rootId)
    {
        var result = new List<string>();

        if (node.Id == rootId)
        {
            if (node.Childs != null)
            {
                foreach (var child in node.Childs)
                {
                    if (!string.IsNullOrEmpty(child.Title))
                    {
                        result.Add(child.Title.ToLowerInvariant());
                    }
                }
            }
            return result;
        }

        if (node.Childs != null)
        {
            foreach (var child in node.Childs)
            {
                result.AddRange(FindTopicsUnderRoot(child, rootId));
            }
        }

        return result;
    }
}
