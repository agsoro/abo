using Abo.Core.Connectors;
using Abo.Integrations.XpectoLive.Models;

namespace Abo.Integrations.XpectoLive;

public class XpectoLiveWikiConnector : IWikiConnector
{
    private readonly IXpectoLiveWikiClient _client;
    private readonly string _spaceId;

    public XpectoLiveWikiConnector(IXpectoLiveWikiClient client, string spaceId)
    {
        _client = client;
        _spaceId = string.IsNullOrWhiteSpace(spaceId) ? throw new ArgumentException("Space ID is required for XpectoLive Wiki") : spaceId;
    }

    public async Task<string> GetPageAsync(string path)
    {
        try
        {
            var page = await _client.GetPageAsync(_spaceId, path);
            return page.Content ?? "No Content";
        }
        catch (Exception ex) { return $"Error getting wiki page: {ex.Message}"; }
    }

    public async Task<string> CreatePageAsync(string title, string content, string? parentPath = null)
    {
        try
        {
            var newPage = new PageNew { Title = title, ParentId = parentPath };
            var created = await _client.CreatePageAsync(_spaceId, newPage);
            
            if (created.Id != null && !string.IsNullOrEmpty(content))
            {
                await _client.UpdatePageDraftAsync(_spaceId, created.Id, new ContentUpdate { Content = content });
                await _client.PublishPageDraftAsync(_spaceId, created.Id);
            }
            return $"Successfully created wiki page '{title}' with ID: {created.Id}";
        }
        catch (Exception ex) { return $"Error creating wiki page: {ex.Message}"; }
    }

    public async Task<string> UpdatePageAsync(string path, string content)
    {
        try
        {
            await _client.UpdatePageDraftAsync(_spaceId, path, new ContentUpdate { Content = content });
            var pub = await _client.PublishPageDraftAsync(_spaceId, path);
            return $"Successfully updated wiki page with ID: {pub.Id}";
        }
        catch (Exception ex) { return $"Error updating wiki page: {ex.Message}"; }
    }

    public async Task<string> SearchPagesAsync(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return "Error: Search query cannot be empty.";

            var info = await _client.GetSpaceInfoAsync(_spaceId);
            var results = info.Where(p => (p.PageTitle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                              .Select(p => $"{p.PageID} ({p.PageTitle})");
            
            if (!results.Any()) return $"No pages found in space '{_spaceId}' matching '{query}'.";
            
            return $"Found in {results.Count()} pages:\n- " + string.Join("\n- ", results);
        }
        catch (Exception ex) { return $"Error searching wiki pages: {ex.Message}"; }
    }
}
