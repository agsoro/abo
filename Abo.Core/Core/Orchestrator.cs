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
    private readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "Data", "llm_traffic.jsonl");
    private readonly string _consumptionLogPath = Path.Combine(AppContext.BaseDirectory, "Data", "llm_consumption.jsonl");

    public Orchestrator(HttpClient httpClient, IConfiguration configuration, ILogger<Orchestrator> logger, SessionService sessionService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _sessionService = sessionService;

        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
    }

    public List<ChatMessage> GetSessionHistory(string sessionId)
    {
        return _sessionService.GetHistory(sessionId);
    }

    public async Task<string> RunAgentLoopAsync(IAgent agent, string userMessage, string sessionId, string? userName = null, string? userId = null)
    {
        var apiEndpoint = _configuration["Config:ApiEndpoint"] ?? throw new InvalidOperationException("API Endpoint not configured.");
        var apiKey = _configuration["Config:ApiKey"] ?? string.Empty;
        var defaultLanguage = _configuration["Config:DefaultLanguage"] ?? "en-us";

        // Retrieve existing history
        var history = _sessionService.GetHistory(sessionId);

        // Add new user message to persistent history immediately
        var userMsg = new ChatMessage { Role = "user", Content = userMessage };
        _sessionService.AddMessage(sessionId, userMsg);

        // Prepare the request with full history + current system prompt
        var requestMessages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = $"{agent.SystemPrompt}\n\n[CONTEXT] The default language for all responses '{defaultLanguage}', code/docu is 'en-us'" }
        };

        lock (history)
        {
            requestMessages.AddRange(history);
        }

        var request = new ChatCompletionRequest
        {
            Model = _configuration["Config:ModelName"] ?? "anthropic/claude-haiku-4.5",
            Messages = requestMessages,
            Tools = agent.GetToolDefinitions()
        };

        if (request.Tools?.Count == 0)
        {
            request.Tools = null;
        }

        int maxLoops = 60;
        int currentLoop = 0;
        string? lastQuestionOutput = null;
        string? accumulatedContent = null;

        // Usage tracking accumulators for this agent loop run
        int totalCalls = 0;
        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        double totalCost = 0.0;
        string currentModelName = request.Model;
        bool terminateAfterSynthesis = false;

        try
        {
            while (currentLoop < maxLoops)
            {
                currentLoop++;

                // Recalculate model in case agent state changed
                currentModelName = _configuration["Config:ModelName"] ?? "anthropic/claude-haiku-4.5";
                if (agent.RequiresReviewModel && !string.IsNullOrEmpty(_configuration["Config:ReviewModelName"]))
                {
                    currentModelName = _configuration["Config:ReviewModelName"]!;
                }
                else if (agent.RequiresCapableModel && !string.IsNullOrEmpty(_configuration["Config:CapableModelName"]))
                {
                    currentModelName = _configuration["Config:CapableModelName"]!;
                }
                request.Model = currentModelName;

                // Auto summarize if request.Messages is getting long
                if (request.Messages.Count > 80)
                {
                    int keepTailCount = 4;
                    int splitIndex = request.Messages.Count - keepTailCount;

                    while (splitIndex < request.Messages.Count)
                    {
                        var msg = request.Messages[splitIndex];
                        if (msg.Role == "tool")
                        {
                            splitIndex++;
                        }
                        else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                        {
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (splitIndex > 1 && splitIndex < request.Messages.Count)
                    {
                        _logger.LogInformation($"[Session: {sessionId}] History reached {request.Messages.Count} messages. Summarizing older context...");
                        var messagesToSummarize = request.Messages.GetRange(1, splitIndex - 1);
                        var summaryText = await SummarizeMessagesAsync(messagesToSummarize, currentModelName, sessionId);

                        var summaryMessage = new ChatMessage
                        {
                            Role = "user",
                            Content = "Here is a summary of the earlier conversation and actions for context:\n\n" + summaryText
                        };

                        var newRequestMessages = new List<ChatMessage> { request.Messages[0] };
                        newRequestMessages.Add(summaryMessage);
                        newRequestMessages.AddRange(request.Messages.Skip(splitIndex));

                        request.Messages = newRequestMessages;

                        var newHistory = new List<ChatMessage>();
                        newHistory.Add(summaryMessage);
                        newHistory.AddRange(request.Messages.Skip(splitIndex));
                        _sessionService.ReplaceHistory(sessionId, newHistory);
                    }
                }

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

                // Accumulate usage data if available
                totalCalls++;
                if (aiResponse?.Usage != null)
                {
                    totalInputTokens += aiResponse.Usage.PromptTokens;
                    totalOutputTokens += aiResponse.Usage.CompletionTokens;
                    totalCost += aiResponse.Usage.Cost ?? 0.0;
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
                        try
                        {
                            toolResult = await agent.HandleToolCallAsync(toolCall);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Tool {toolCall.Function.Name} failed.");
                            toolResult = $"Error: {ex.Message}";
                        }

                        if (toolCall.Function.Name == "ask_multiple_choice" || toolCall.Function.Name == "ask_quiz_question")
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

                        // Sentinel pattern: SpecialistAgent.complete_task returns [COMPLETE_TASK_RESULT]:<resultNotes>
                        // on success. Detect it here and immediately surface the resultNotes to the caller,
                        // eliminating the extra LLM synthesis round-trip. Mirrors the [TERMINATE_MANAGER_LOOP] pattern.
                        if (toolResult.StartsWith(AgentSentinels.CompleteTaskResult))
                        {
                            var resultNotes = toolResult.Substring(AgentSentinels.CompleteTaskResult.Length);
                            _logger.LogInformation($"[Session: {sessionId}] complete_task sentinel detected. Terminating agent loop immediately.");
                            await LogConsumptionAsync(sessionId, currentModelName, totalCalls, totalInputTokens, totalOutputTokens, totalCost);
                            return resultNotes;
                        }

                        if ((agent.Name == "SpecialistAgent" && toolCall.Function.Name == "complete_task") ||
                            (agent.Name == "ManagerAgent" && toolCall.Function.Name == "delegate_task"))
                        {
                            terminateAfterSynthesis = true;
                        }
                    }

                    // CRITICAL: Continue the loop to let the model synthesize the results
                    continue;
                }

                // Final answer reached – log consumption for this run
                await LogConsumptionAsync(sessionId, currentModelName, totalCalls, totalInputTokens, totalOutputTokens, totalCost);

                var finalContent = choice.Message.Content ?? accumulatedContent;

                if (!string.IsNullOrEmpty(lastQuestionOutput) &&
                    (string.IsNullOrEmpty(finalContent) || (!finalContent.Contains("1.") && !finalContent.Contains("**1.**"))))
                {
                    finalContent = (finalContent ?? "").Trim() + "\n\n" + lastQuestionOutput;
                }

                _logger.LogInformation($"[Session: {sessionId}] Raw model output: '{finalContent}'");

                var finalOutput = finalContent?.Trim() ?? "(Empty content)";

                if (terminateAfterSynthesis)
                {
                    return finalOutput;
                }

                if (terminateAfterSynthesis || choice.Message.ToolCalls == null || choice.Message.ToolCalls.Count == 0)
                {
                    return finalOutput;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[Session: {sessionId}] Fatal error in Agent Loop");
            // Still try to log whatever consumption we've accumulated
            if (totalCalls > 0)
            {
                await LogConsumptionAsync(sessionId, currentModelName, totalCalls, totalInputTokens, totalOutputTokens, totalCost);
            }
            return $"Error: {ex.Message}";
        }

        // Max loops reached – log consumption
        if (totalCalls > 0)
        {
            await LogConsumptionAsync(sessionId, currentModelName, totalCalls, totalInputTokens, totalOutputTokens, totalCost);
        }

        return "Error: Max agent loops reached.";
    }

    private async Task<string> SummarizeMessagesAsync(List<ChatMessage> messages, string modelName, string sessionId)
    {
        var apiEndpoint = _configuration["Config:ApiEndpoint"] ?? throw new InvalidOperationException("API Endpoint not configured.");
        var apiKey = _configuration["Config:ApiKey"] ?? string.Empty;

        var contentList = new List<string>();
        foreach (var m in messages)
        {
            var text = m.Content ?? "";
            if (m.ToolCalls != null && m.ToolCalls.Any())
            {
                var toolsString = string.Join(", ", m.ToolCalls.Select(tc => $"{tc.Function.Name}({tc.Function.Arguments})"));
                text += $"\n[Tool Calls: {toolsString}]";
            }
            contentList.Add($"[{m.Role.ToUpper()}]: {text}");
        }
        var content = string.Join("\n\n", contentList);

        var prompt = "Please provide a concise but highly detailed summary of the following sequence of messages and tool executions. " +
                     "Keep important context, paths, IDs, findings, and code snippets, as the agent relies on this to continue its task. " +
                     "Just output the summary, no intro/outro.\n\n" + content;

        var request = new ChatCompletionRequest
        {
            Model = modelName,
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = "You are a specialized summarizer. Compress the context perfectly." },
                new ChatMessage { Role = "user", Content = prompt }
            },
            MaxTokens = 2000,
            Temperature = 0.0f
        };

        var jsonRequest = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });

        await LogTrafficAsync(sessionId, "SUMMARY_REQUEST", jsonRequest);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiEndpoint)
        {
            Content = new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
        }

        try
        {
            var httpResponse = await _httpClient.SendAsync(httpRequest);
            var responseString = await httpResponse.Content.ReadAsStringAsync();
            await LogTrafficAsync(sessionId, "SUMMARY_RESPONSE", responseString);

            if (httpResponse.IsSuccessStatusCode)
            {
                var aiResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseString);
                var choice = aiResponse?.Choices.FirstOrDefault();
                return choice?.Message.Content?.Trim() ?? "Summary failed.";
            }
            else
            {
                _logger.LogWarning($"Summary API failed: {httpResponse.StatusCode} - {responseString}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing history.");
        }

        return "Summary failed.";
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

    private async Task LogConsumptionAsync(string sessionId, string model, int callCount, int inputTokens, int outputTokens, double totalCost)
    {
        try
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                SessionId = sessionId,
                Model = model,
                CallCount = callCount,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                TotalCost = totalCost
            };
            var line = JsonSerializer.Serialize(logEntry) + Environment.NewLine;
            await File.AppendAllTextAsync(_consumptionLogPath, line);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write LLM consumption log.");
        }
    }
}
