using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Abo.Integrations.XpectoLive.Models;

namespace Abo.Integrations.XpectoLive;

public class XpectoLiveWikiClient : IXpectoLiveWikiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<XpectoLiveWikiClient> _logger;

    public XpectoLiveWikiClient(HttpClient httpClient, IOptions<XpectoLiveOptions> options, ILogger<XpectoLiveWikiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var config = options.Value;
        
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(config.BaseUrl);
        }
        
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
        }
    }

    public async Task<Space[]> GetSpacesAsync()
    {
        _logger.LogInformation("Fetching wiki spaces.");
        var response = await _httpClient.GetAsync("/backoffice/api/v1/wiki/spaces");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Space[]>() ?? Array.Empty<Space>();
    }

    public async Task<Space> CreateSpaceAsync(SpaceNew spaceNew)
    {
        _logger.LogInformation("Creating new wiki space.");
        var response = await _httpClient.PutAsJsonAsync("/backoffice/api/v1/wiki/spaces", spaceNew);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {response.StatusCode} - {err}");
        }
        return await response.Content.ReadFromJsonAsync<Space>() ?? new Space();
    }

    public async Task<Space> GetSpaceAsync(string spaceId)
    {
        _logger.LogInformation($"Fetching wiki space {spaceId}.");
        var response = await _httpClient.GetAsync($"/backoffice/api/v1/wiki/space/{spaceId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Space>() ?? new Space();
    }

    public async Task<SpacePageInfo[]> GetSpaceInfoAsync(string spaceId)
    {
        _logger.LogInformation($"Fetching info for wiki space {spaceId}.");
        var response = await _httpClient.GetAsync($"/backoffice/api/v1/wiki/space/info/{spaceId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SpacePageInfo[]>() ?? Array.Empty<SpacePageInfo>();
    }

    public async Task<Page> CreatePageAsync(string spaceId, PageNew pageNew)
    {
        _logger.LogInformation($"Creating new page in space {spaceId}.");
        var response = await _httpClient.PutAsJsonAsync($"/backoffice/api/v1/wiki/page/{spaceId}", pageNew);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Page>() ?? new Page();
    }

    public async Task<Page> GetPageAsync(string spaceId, string pageId)
    {
        _logger.LogInformation($"Fetching wiki page {pageId} in space {spaceId}.");
        var response = await _httpClient.GetAsync($"/backoffice/api/v1/wiki/page/{spaceId}/{pageId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Page>() ?? new Page();
    }

    public async Task<Page> UpdatePageDraftAsync(string spaceId, string pageId, ContentUpdate contentUpdate)
    {
        _logger.LogInformation($"Updating draft for wiki page {pageId} in space {spaceId}.");
        var response = await _httpClient.PatchAsJsonAsync($"/backoffice/api/v1/wiki/page/{spaceId}/{pageId}/draft", contentUpdate);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Page>() ?? new Page();
    }

    public async Task<Page> PublishPageDraftAsync(string spaceId, string pageId)
    {
        _logger.LogInformation($"Publishing draft for wiki page {pageId} in space {spaceId}.");
        var response = await _httpClient.PostAsync($"/backoffice/api/v1/wiki/page/{spaceId}/{pageId}/draft", null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Page>() ?? new Page();
    }

    public async Task MovePageAsync(string spaceId, string pageId, MovePageRequest moveRequest)
    {
        _logger.LogInformation($"Moving wiki page {pageId} in space {spaceId}.");
        var response = await _httpClient.PostAsJsonAsync($"/backoffice/api/v1/wiki/page/{spaceId}/{pageId}/move", moveRequest);
        response.EnsureSuccessStatusCode();
    }

    public async Task CopyPageAsync(string spaceId, string pageId, CopyPageRequest copyRequest)
    {
        _logger.LogInformation($"Copying wiki page {pageId} in space {spaceId}.");
        var response = await _httpClient.PostAsJsonAsync($"/backoffice/api/v1/wiki/page/{spaceId}/{pageId}/copy", copyRequest);
        response.EnsureSuccessStatusCode();
    }

    public async Task JoinCollaborativeRoomAsync(string spaceId, string pageId, string clientId)
    {
        _logger.LogInformation($"Client {clientId} joining collaborative room for page {pageId} in space {spaceId}.");
        var response = await _httpClient.PostAsync($"/backoffice/api/v1/wiki/collaborative/room/{spaceId}/{pageId}/{clientId}", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task LeaveCollaborativeRoomAsync(string spaceId, string pageId, string clientId)
    {
        _logger.LogInformation($"Client {clientId} leaving collaborative room for page {pageId} in space {spaceId}.");
        var response = await _httpClient.PostAsync($"/backoffice/api/v1/wiki/collaborative/room/{spaceId}/{pageId}/{clientId}/leave", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RdpAsync(string domain, string user, string computerName)
    {
        _logger.LogInformation($"Initializing RDP for {domain}\\{user} on {computerName}.");
        var response = await _httpClient.GetAsync($"/backoffice/api/v1/wiki/rdp/{domain}/{user}/{computerName}");
        response.EnsureSuccessStatusCode();
    }
}
