using System.Text.Json;
using Abo.Agents;
using Abo.Contracts.OpenAI;

namespace Abo.Core;

public class AgentSupervisor
{
    private readonly IEnumerable<IAgent> _agents;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentSupervisor> _logger;

    public AgentSupervisor(IEnumerable<IAgent> agents, HttpClient httpClient, IConfiguration configuration, ILogger<AgentSupervisor> logger)
    {
        _agents = agents;
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IAgent> GetBestAgentAsync(string userMessage)
    {
        _logger.LogInformation($"Determining best agent for message: '{userMessage}'");
        
        var apiEndpoint = _configuration["Config:ApiEndpoint"];
        var modelName = _configuration["Config:ModelName"];
        var apiKey = _configuration["Config:ApiKey"];

        if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(modelName))
        {
            _logger.LogWarning("API configuration missing for supervisor. Defaulting to first agent.");
            return _agents.First();
        }

        var agentList = string.Join("\n", _agents.Select(a => $"- {a.Name}: {a.Description}"));

        var systemPrompt = 
            "You are the Agent Supervisor. Your job is to select the BEST agent to handle a user's request. " +
            "You must return ONLY the name of the agent. No other text.\n\n" +
            "AVAILABLE AGENTS:\n" + agentList;

        var request = new ChatCompletionRequest
        {
            Model = modelName,
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = $"User Message: '{userMessage}'\nSelected Agent Name:" }
            },
            MaxTokens = 20,
            Temperature = 0
        };

        try
        {
            var jsonRequest = JsonSerializer.Serialize(request);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiEndpoint)
            {
                Content = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json")
            };

            if (!string.IsNullOrEmpty(apiKey))
            {
                httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
            }

            var response = await _httpClient.SendAsync(httpRequest);
            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var aiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseString);
                var selectedName = aiResponse?.Choices.FirstOrDefault()?.Message.Content?.Trim();

                _logger.LogInformation($"Supervisor selected: {selectedName}");

                var matchedAgent = _agents.FirstOrDefault(a => a.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
                if (matchedAgent != null) return matchedAgent;
            }
            else
            {
                _logger.LogWarning($"Supervisor API call failed: {responseString}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AgentSupervisor selection.");
        }

        _logger.LogWarning("Could not reliably select agent. Falling back to HelloWorldAgent.");
        return _agents.First(a => a.Name == "HelloWorldAgent");
    }
}
