using System.Text.Json.Serialization;

namespace Abo.Integrations.Mattermost;

public class MattermostWebSocketEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public MattermostEventData? Data { get; set; }

    [JsonPropertyName("broadcast")]
    public MattermostBroadcastData? Broadcast { get; set; }
}

public class MattermostBroadcastData
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
}

public class MattermostEventData
{
    [JsonPropertyName("post")]
    public string PostJson { get; set; } = string.Empty; // Mattermost sends the post as an inner JSON string
    
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
}

public class MattermostPost
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("channel_id")]
    public string ChannelId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
    
    [JsonPropertyName("root_id")]
    public string RootId { get; set; } = string.Empty; // Identifies if it's part of a thread
}
