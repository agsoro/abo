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

/// <summary>
/// Outgoing WebSocket action payload for Mattermost (e.g. typing indicator).
/// Format: { "action": "user_typing", "seq": 1, "data": { "channel_id": "...", "parent_id": "..." } }
/// </summary>
public class MattermostWebSocketAction
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("seq")]
    public int Seq { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, string> Data { get; set; } = new();
}
