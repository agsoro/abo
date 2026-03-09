using System.Text.Json;
using Abo.Agents;
using Abo.Contracts.OpenAI;

namespace Abo.Core;

public class Orchestrator
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Orchestrator> _logger;

    private readonly SessionService _sessionService;
    private readonly string _logPath = "Data/llm_traffic.jsonl";

    public Orchestrator(HttpClient httpClient, IConfiguration configuration, ILogger<Orchestrator> logger, SessionService sessionService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _sessionService = sessionService;
        
        var dir = Path.GetDirectoryName(_logPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
    }

    public async Task<string> RunAgentLoopAsync(IAgent agent, string userMessage, string sessionId, string? userName = null)
    {
        var apiEndpoint = _configuration["Config:ApiEndpoint"] ?? throw new InvalidOperationException("API Endpoint not configured.");
        var modelName = _configuration["Config:ModelName"] ?? throw new InvalidOperationException("Model Name not configured.");
        var apiKey = _configuration["Config:ApiKey"] ?? string.Empty;

        // Retrieve existing history
        var history = _sessionService.GetHistory(sessionId);
        
        // Add new user message to persistent history immediately
        var userMsg = new ChatMessage { Role = "user", Content = userMessage };
        _sessionService.AddMessage(sessionId, userMsg);

        // Prepare the request with full history + current system prompt
        var requestMessages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = $"{agent.SystemPrompt}\n\n[CONTEXT] Current Session/Channel ID: {sessionId}\n[CONTEXT] User Name: {userName ?? "Unknown"}" }
        };
        
        lock (history)
        {
            requestMessages.AddRange(history);
        }

        var request = new ChatCompletionRequest
        {
            Model = modelName,
            Messages = requestMessages,
            Tools = agent.GetToolDefinitions()
        };

        if (request.Tools?.Count == 0)
        {
            request.Tools = null; 
        }

        int maxLoops = 5;
        int currentLoop = 0;
        string? lastQuestionOutput = null;
        string? accumulatedContent = null;

        try
        {
            while (currentLoop < maxLoops)
            {
                currentLoop++;
                
                _logger.LogInformation($"[Session: {sessionId}] [Loop {currentLoop}] Sending request to {apiEndpoint}");

                var jsonRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
                await LogTrafficAsync(sessionId, "REQUEST", jsonRequest);
                
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiEndpoint)
                {
                    Content = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrEmpty(apiKey))
                {
                    httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                }

                var httpResponse = await _httpClient.SendAsync(httpRequest);
                var responseString = await httpResponse.Content.ReadAsStringAsync();

                if (!httpResponse.IsSuccessStatusCode)
                {
                    _logger.LogError($"API Error: {responseString}");
                    await LogTrafficAsync(sessionId, "ERROR", responseString);
                    return $"Error: {httpResponse.StatusCode} - {responseString}";
                }

                await LogTrafficAsync(sessionId, "RESPONSE", responseString);

                var aiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseString);
                var choice = aiResponse?.Choices.FirstOrDefault();

                if (choice == null)
                {
                    return "Error: No response from model.";
                }

                // Append assistant's response to history and current request context
                _sessionService.AddMessage(sessionId, choice.Message);
                request.Messages.Add(choice.Message);

                // Capture content if provided
                if (!string.IsNullOrEmpty(choice.Message.Content))
                {
                    accumulatedContent = (accumulatedContent == null) 
                        ? choice.Message.Content 
                        : accumulatedContent + "\n" + choice.Message.Content;
                }

                if (choice.Message.ToolCalls != null && choice.Message.ToolCalls.Count > 0)
                {
                    _logger.LogInformation($"[Loop {currentLoop}] Model requested {choice.Message.ToolCalls.Count} tool call(s).");

                    foreach (var toolCall in choice.Message.ToolCalls)
                    {
                        _logger.LogInformation($"Executing Tool: {toolCall.Function.Name}");
                        var toolResult = "Error executing tool.";
                        try {
                            toolResult = await agent.HandleToolCallAsync(toolCall);
                        } catch (Exception ex) {
                            _logger.LogError(ex, $"Tool {toolCall.Function.Name} failed.");
                            toolResult = $"Error: {ex.Message}";
                        }
                        
                        if (toolCall.Function.Name == "ask_multiple_choice")
                        {
                            lastQuestionOutput = toolResult;
                        }
                        
                        var toolResponseMsg = new ChatMessage
                        {
                            Role = "tool",
                            ToolCallId = toolCall.Id,
                            Content = toolResult
                        };

                        _sessionService.AddMessage(sessionId, toolResponseMsg);
                        request.Messages.Add(toolResponseMsg);
                    }
                    
                    // CRITICAL: Continue the loop to let the model synthesize the results
                    continue;
                }

                // Final answer reached
                var finalContent = choice.Message.Content ?? accumulatedContent;
                
                // If we have a question output from a tool call in this session, 
                // and the final content doesn't seem to contain it (no numbered list), append it.
                if (!string.IsNullOrEmpty(lastQuestionOutput) && 
                    (string.IsNullOrEmpty(finalContent) || (!finalContent.Contains("1.") && !finalContent.Contains("**1.**"))))
                {
                    finalContent = (finalContent ?? "").Trim() + "\n\n" + lastQuestionOutput;
                }

                _logger.LogInformation($"[Session: {sessionId}] Raw model output: '{finalContent}'");
                return finalContent?.Trim() ?? "(Empty content)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Session: {sessionId}] Fatal error in Agent Loop");
            return $"Error: {ex.Message}";
        }

        return "Error: Max agent loops reached.";
    }

    private async Task LogTrafficAsync(string sessionId, string type, string content)
    {
        try
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                SessionId = sessionId,
                Type = type,
                Content = content
            };
            var line = JsonSerializer.Serialize(logEntry) + Environment.NewLine;
            await File.AppendAllTextAsync(_logPath, line);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write LLM traffic log.");
        }
    }
}
