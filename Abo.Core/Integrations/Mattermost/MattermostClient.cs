using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Abo.Integrations.Mattermost;

public class MattermostClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MattermostClient> _logger;
    private readonly MattermostOptions _options;

    public MattermostClient(HttpClient httpClient, IOptions<MattermostOptions> options, ILogger<MattermostClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        if (!string.IsNullOrEmpty(_options.BaseUrl))
        {
            var baseUrl = _options.BaseUrl;
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        if (!string.IsNullOrEmpty(_options.BotToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.BotToken);
        }
    }

    /// <summary>
    /// Posts a message directly to a Mattermost channel or DM thread using the REST API.
    /// Requires BotToken to be set.
    /// </summary>
    public async Task<bool> SendMessageAsync(string channelId, string message, string? rootId = null)
    {
        if (string.IsNullOrEmpty(_options.BotToken) || string.IsNullOrEmpty(_options.BaseUrl))
        {
            _logger.LogWarning("Mattermost BaseUrl or BotToken is not configured.");
            return false;
        }

        try
        {
            _logger.LogInformation($"Sending REST message to Mattermost channel {channelId}...");
            
            var payload = new 
            { 
                channel_id = channelId, 
                message = message,
                root_id = rootId // Use this to reply in a specific thread
            };
            
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            
            // BaseUrl is usually configured as https://your-server.com/api/v4/
            // Appending "posts" correctly yields https://your-server.com/api/v4/posts.
            // (A leading slash "/posts" would overwrite the /api/v4 base path!)
            var response = await _httpClient.PostAsync("posts", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Mattermost API error: {response.StatusCode} - {error}");
            }
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Mattermost REST message.");
            return false;
        }
    }

    /// <summary>
    /// Sends a "user is typing" indicator to a Mattermost channel via the REST API.
    /// NOTE: This REST endpoint is unreliable for bot tokens on many Mattermost server
    /// configurations. Typing indicators should preferably be sent via the authenticated
    /// WebSocket connection (action: "user_typing") — see MattermostListenerService.
    /// This method is retained as a diagnostic fallback and logs the HTTP response status.
    /// </summary>
    public async Task SendTypingAsync(string channelId, string? parentId = null)
    {
        if (string.IsNullOrEmpty(_options.BotToken) || string.IsNullOrEmpty(_options.BaseUrl))
        {
            _logger.LogWarning("SendTypingAsync: BotToken or BaseUrl is not configured, skipping.");
            return;
        }

        try
        {
            // Omit parent_id entirely when empty to avoid API rejection.
            object payload = string.IsNullOrEmpty(parentId)
                ? new { channel_id = channelId }
                : new { channel_id = channelId, parent_id = parentId };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("users/me/typing", content);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "SendTypingAsync REST call failed with {StatusCode}: {Body}",
                    response.StatusCode, body);
            }
            else
            {
                _logger.LogDebug("SendTypingAsync REST call succeeded for channel {ChannelId}.", channelId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send typing indicator via REST.");
        }
    }

    /// <summary>
    /// Fetches the username for a given user ID.
    /// </summary>
    public async Task<string> GetUsernameAsync(string userId)
    {
        if (string.IsNullOrEmpty(_options.BotToken) || string.IsNullOrEmpty(_options.BaseUrl))
        {
            return "UnknownUser";
        }

        try
        {
            // API: GET /api/v4/users/{user_id}
            var response = await _httpClient.GetAsync($"users/{userId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var user = JsonSerializer.Deserialize<JsonElement>(content);
                if (user.TryGetProperty("username", out var usernameProp))
                {
                    return usernameProp.GetString() ?? "UnknownUser";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to fetch username for {userId}");
        }
        
        return "UnknownUser";
    }

    /// <summary>
    /// Fetches the user ID for a given username.
    /// </summary>
    public async Task<string?> GetUserIdByUsernameAsync(string username)
    {
        if (string.IsNullOrEmpty(_options.BotToken) || string.IsNullOrEmpty(_options.BaseUrl))
            return null;

        try
        {
            var response = await _httpClient.GetAsync($"users/username/{username}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var user = JsonSerializer.Deserialize<JsonElement>(content);
                if (user.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to fetch user ID for username {username}");
        }
        
        return null;
    }

    /// <summary>
    /// Fetches the current bot's user ID.
    /// </summary>
    public async Task<string?> GetMeAsync()
    {
        if (string.IsNullOrEmpty(_options.BotToken) || string.IsNullOrEmpty(_options.BaseUrl))
            return null;

        try
        {
            var response = await _httpClient.GetAsync("users/me");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var user = JsonSerializer.Deserialize<JsonElement>(content);
                if (user.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bot user ID (/users/me)");
        }
        
        return null;
    }

    /// <summary>
    /// Gets or creates a direct message channel between the bot and the specified user.
    /// </summary>
    public async Task<string?> GetDirectChannelAsync(string botUserId, string targetUserId)
    {
        if (string.IsNullOrEmpty(_options.BotToken) || string.IsNullOrEmpty(_options.BaseUrl))
            return null;

        try
        {
            var payload = new[] { botUserId, targetUserId };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("channels/direct", content);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var channel = JsonSerializer.Deserialize<JsonElement>(responseContent);
                if (channel.TryGetProperty("id", out var idProp))
                {
                    return idProp.GetString();
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Mattermost API error getting direct channel: {response.StatusCode} - {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get direct channel.");
        }
        
        return null;
    }

    /// <summary>
    /// Sends a direct message to a user by resolving their username.
    /// </summary>
    public async Task<bool> SendDirectMessageAsync(string username, string message)
    {
        var targetUserId = await GetUserIdByUsernameAsync(username);
        if (string.IsNullOrEmpty(targetUserId))
        {
            _logger.LogWarning($"Could not resolve Mattermost username '{username}' to send direct message.");
            return false;
        }

        var botUserId = await GetMeAsync();
        if (string.IsNullOrEmpty(botUserId))
        {
            _logger.LogWarning("Could not resolve Mattermost Bot User ID to send direct message.");
            return false;
        }

        var channelId = await GetDirectChannelAsync(botUserId, targetUserId);
        if (string.IsNullOrEmpty(channelId))
        {
            _logger.LogWarning($"Could not create or retrieve direct message channel for bot and user '{username}'.");
            return false;
        }

        return await SendMessageAsync(channelId, message);
    }
}
