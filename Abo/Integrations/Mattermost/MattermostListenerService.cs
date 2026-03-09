using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Abo.Agents;
using Abo.Core;
using Microsoft.Extensions.Options;

namespace Abo.Integrations.Mattermost;

public class MattermostListenerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MattermostListenerService> _logger;
    private readonly MattermostOptions _options;
    
    private string? _botUserId;

    public MattermostListenerService(
        IServiceProvider serviceProvider, 
        IOptions<MattermostOptions> options, 
        ILogger<MattermostListenerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_options.BaseUrl) || string.IsNullOrEmpty(_options.BotToken))
        {
            _logger.LogWarning("Mattermost BaseUrl or BotToken is not configured. Listener service will not start.");
            return;
        }

        // Convert http/https to ws/wss
        var wsUriStr = _options.BaseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        if (!wsUriStr.EndsWith("/")) wsUriStr += "/";
        wsUriStr += "websocket";

        var uri = new Uri(wsUriStr);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Authorization", $"Bearer {_options.BotToken}");

                _logger.LogInformation($"Connecting to Mattermost WebSocket: {uri}");
                await ws.ConnectAsync(uri, stoppingToken);
                _logger.LogInformation("Successfully connected to Mattermost WebSocket.");

                var buffer = new byte[8192];
                var sb = new StringBuilder();

                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("Mattermost WebSocket closed by server.");
                        break;
                    }

                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = sb.ToString();
                        sb.Clear();
                        
                        // Fire and forget processing to avoid blocking the listen loop
                        _ = Task.Run(() => HandleMessageAsync(json, stoppingToken), stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Mattermost WebSocket connection error. Retrying in 10 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task HandleMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation($"RAW WS JSON: {json.Substring(0, Math.Min(json.Length, 200))}...");

            var wsEvent = JsonSerializer.Deserialize<MattermostWebSocketEvent>(json);
            _logger.LogInformation($"Event received: {wsEvent?.Event}");
            
            // Capture Bot User ID from hello or status events if we haven't yet
            if (string.IsNullOrEmpty(_botUserId) && wsEvent?.Event == "hello" && wsEvent.Broadcast?.UserId != null)
            {
                _botUserId = wsEvent.Broadcast.UserId;
                _logger.LogInformation($"Captured Bot User ID: {_botUserId}");
            }
            if (string.IsNullOrEmpty(_botUserId) && wsEvent?.Event == "status_change" && wsEvent.Data?.UserId != null)
            {
                _botUserId = wsEvent.Data.UserId;
                _logger.LogInformation($"Captured Bot User ID from status: {_botUserId}");
            }

            // Only react to new posts
            if (wsEvent?.Event != "posted")
            {
                _logger.LogDebug($"Ignoring non-posted event: {wsEvent?.Event}");
                return;
            }

            if (wsEvent.Data == null || string.IsNullOrEmpty(wsEvent.Data.PostJson))
            {
                _logger.LogWarning($"Posted event missing data/postJson: {json}");
                return;
            }

            var post = JsonSerializer.Deserialize<MattermostPost>(wsEvent.Data.PostJson);
            if (post == null || string.IsNullOrWhiteSpace(post.Message)) 
            {
                _logger.LogWarning("Post deserialized to null or empty message.");
                return;
            }

            // Simple prevention of endless bot loops
            if (!string.IsNullOrEmpty(_botUserId) && post.UserId == _botUserId)
            {
                _logger.LogInformation("Ignoring message from self to prevent loop.");
                return;
            }

            // Simple prevention of endless bot loops:
            // In a real app, you'd check `post.UserId` against your bot's own ID
            // Here, we'll avoid processing anything generated via UI or obvious bot prefixes
            // For now, let's just log and process it to ensure it answers
            _logger.LogInformation($"Received message in channel {post.ChannelId}: {post.Message}");

            // Spawning a background scope so we can use Scoped/Transient DI services
            using var scope = _serviceProvider.CreateScope();
            
            // Re-resolve transient Orchestrator and Agent
            var orchestrator = scope.ServiceProvider.GetRequiredService<Orchestrator>();
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisor>();
            var mattermostClient = scope.ServiceProvider.GetRequiredService<MattermostClient>();

            // Fetch actual username
            var userName = await mattermostClient.GetUsernameAsync(post.UserId);
            _logger.LogInformation($"Resolved sender username: {userName}");

            // Intelligent selection with context
            var history = orchestrator.GetSessionHistory(post.ChannelId);
            var agent = await supervisor.GetBestAgentAsync(post.Message, history);

            _logger.LogInformation($"Invoking Orchestrator with {agent.Name} on received message...");
            var result = await orchestrator.RunAgentLoopAsync(agent, post.Message, post.ChannelId, userName);
            
            _logger.LogInformation("Orchestrator produced reply, sending to Mattermost...");
            
            // Reply: Only use rootId if it already exists (don't force new threads in DMs)
            await mattermostClient.SendMessageAsync(post.ChannelId, result, post.RootId);

        }
        catch (JsonException)
        {
            // Ignore parse errors from non-post system events
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Mattermost message payload.");
        }
    }
}
