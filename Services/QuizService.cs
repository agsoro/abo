using System.Text.Json;
using Abo.Agents;
using Abo.Core;
using Abo.Integrations.Mattermost;

namespace Abo.Services;

public class QuizService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuizService> _logger;

    private string SubscriptionsPath => "Data/quiz_subscriptions.json";

    public QuizService(IServiceProvider serviceProvider, ILogger<QuizService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quiz Service is starting.");

        // Wait for a bit to let other services settle
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TriggerQuestionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering quiz questions.");
            }

            // Wait for 1 hour
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task TriggerQuestionsAsync(CancellationToken stoppingToken)
    {
        if (!File.Exists(SubscriptionsPath)) return;

        var json = await File.ReadAllTextAsync(SubscriptionsPath);
        var subs = JsonSerializer.Deserialize<List<string>>(json);
        if (subs == null || subs.Count == 0) return;

        _logger.LogInformation($"Triggering quiz for {subs.Count} subscribers.");

        using var scope = _serviceProvider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<Orchestrator>();
        var agent = scope.ServiceProvider.GetRequiredService<QuizAgent>();
        var mattermostClient = scope.ServiceProvider.GetRequiredService<MattermostClient>();

        foreach (var channelId in subs)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                _logger.LogInformation($"Sending quiz question to channel {channelId}");
                var response = await orchestrator.RunAgentLoopAsync(agent, "SYSTEM_EVENT: HOURLY_QUESTION_TRIGGER", channelId);
                await mattermostClient.SendMessageAsync(channelId, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send quiz question to channel {channelId}");
            }
        }
    }
}
