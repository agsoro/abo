namespace Abo.Integrations.Mattermost;

public class MattermostOptions
{
    public string BaseUrl { get; set; } = string.Empty; // e.g., https://your-mattermost.com/api/v4
    public string BotToken { get; set; } = string.Empty;
}
