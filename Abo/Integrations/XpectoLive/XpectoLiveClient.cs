using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Abo.Integrations.XpectoLive;

public class XpectoLiveClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<XpectoLiveClient> _logger;

    public XpectoLiveClient(HttpClient httpClient, IOptions<XpectoLiveOptions> options, ILogger<XpectoLiveClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var config = options.Value;
        
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(config.BaseUrl);
        }
        
        // The swagger defines the API Key should be passed in the x-api-key header
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
        }
    }

    /// <summary>
    /// Example method to fetch tickets. To be implemented against the real API.
    /// </summary>
    public async Task<string> GetTicketsAsync(string queryParameters)
    {
        try
        {
            _logger.LogInformation($"Fetching tickets from XpectoLive with query: {queryParameters}");
            
            // Example GET request. Replace with actual endpoint. 
            // var response = await _httpClient.GetAsync($"/api/tickets?{queryParameters}");
            // response.EnsureSuccessStatusCode();
            // return await response.Content.ReadAsStringAsync();

            // Mock response for now:
            await Task.Delay(100);
            return JsonSerializer.Serialize(new[] 
            { 
                new { Id = 1, Title = "Login Bug", Status = "Open" },
                new { Id = 2, Title = "Update Billing", Status = "In Progress"}
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from XpectoLive");
            return $"Error: {ex.Message}";
        }
    }
}
