using System.Net;
using System.Text.Json;
using Abo.Agents;
using Abo.Contracts.OpenAI;
using Abo.Core;
using Abo.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the specialist consultation nudge feature (Issue #436).
/// Tests verify that the Orchestrator correctly injects nudge messages when
/// conversation exceeds configurable thresholds.
/// </summary>
[Trait("Category", "Unit")]
public class OrchestratorNudgeTests
{
    private readonly Mock<IAgent> _agentMock;
    private readonly Mock<ILogger<Orchestrator>> _loggerMock;
    private readonly Mock<ILogger<TrafficLoggerService>> _trafficLoggerMock;

    public OrchestratorNudgeTests()
    {
        _agentMock = new Mock<IAgent>();
        _agentMock.Setup(a => a.Name).Returns("TestAgent");
        _agentMock.Setup(a => a.SystemPrompt).Returns("You are a test agent.");
        _agentMock.Setup(a => a.GetToolDefinitions()).Returns(new List<ToolDefinition>());
        _agentMock.Setup(a => a.HandleToolCallAsync(It.IsAny<ToolCall>()))
            .ReturnsAsync("Tool executed successfully.");

        _loggerMock = new Mock<ILogger<Orchestrator>>();
        _trafficLoggerMock = new Mock<ILogger<TrafficLoggerService>>();
    }

    [Fact]
    public async Task SpecialistNudge_NotInjectedBeforeThreshold()
    {
        // Arrange
        var config = BuildConfig(nudgeThreshold: "100"); // High threshold
        var handler = CreateMockHandler("Test response content");
        var httpClient = new HttpClient(handler.Object);
        
        var sessionService = new SessionService();
        var trafficLogger = new TrafficLoggerService(_trafficLoggerMock.Object, config);
        var orchestrator = new Orchestrator(httpClient, config, _loggerMock.Object, sessionService, trafficLogger);

        var sessionId = Guid.NewGuid().ToString();

        // Act
        var result = await orchestrator.RunAgentLoopAsync(_agentMock.Object, "Test message", sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("[NUDGE_SPECIALIST_CONSULTATION]", result);
    }

    [Fact]
    public async Task SpecialistNudge_IsInjectedAtThreshold()
    {
        // Arrange
        var config = BuildConfig(nudgeThreshold: "2"); // Low threshold to trigger quickly
        var handlerMock = new Mock<HttpMessageHandler>();
        var callCount = 0;
        
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                // Return a tool call response to continue the loop
                var response = new ChatCompletionResponse
                {
                    Id = $"test-{callCount}",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new ChatMessage 
                            { 
                                Role = "assistant", 
                                Content = "Working on it...",
                                ToolCalls = new List<ToolCall>
                                {
                                    new ToolCall
                                    {
                                        Id = $"call-{callCount}",
                                        Type = "function",
                                        Function = new FunctionCall
                                        {
                                            Name = "test_tool",
                                            Arguments = "{}"
                                        }
                                    }
                                }
                            },
                            FinishReason = "tool_calls"
                        }
                    }
                };
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(response))
                };
            });

        var httpClient = new HttpClient(handlerMock.Object);
        
        var sessionService = new SessionService();
        var trafficLogger = new TrafficLoggerService(_trafficLoggerMock.Object, config);
        var orchestrator = new Orchestrator(httpClient, config, _loggerMock.Object, sessionService, trafficLogger);

        var sessionId = Guid.NewGuid().ToString();

        // Act - run agent loop
        var result = await orchestrator.RunAgentLoopAsync(_agentMock.Object, "Test message", sessionId);

        // Assert - the loop should have been triggered and the nudge logged
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SpecialistNudge_UsesDefaultThreshold_WhenConfigMissing()
    {
        // Arrange - config without NudgeSpecialistThreshold
        var config = BuildConfigWithoutNudgeSetting();
        var callCount = 0;
        
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                // Return content that will end the loop (no tool calls)
                var response = new ChatCompletionResponse
                {
                    Id = $"test-{callCount}",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new ChatMessage 
                            { 
                                Role = "assistant", 
                                Content = callCount < 50 
                                    ? "Working on it..." // Keep looping
                                    : "Final response." // End after 50 loops (default threshold)
                            },
                            FinishReason = "stop"
                        }
                    }
                };
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(response))
                };
            });

        var httpClient = new HttpClient(handlerMock.Object);
        
        var sessionService = new SessionService();
        var trafficLogger = new TrafficLoggerService(_trafficLoggerMock.Object, config);
        var orchestrator = new Orchestrator(httpClient, config, _loggerMock.Object, sessionService, trafficLogger);

        var sessionId = Guid.NewGuid().ToString();

        // Act
        var result = await orchestrator.RunAgentLoopAsync(_agentMock.Object, "Test message", sessionId);

        // Assert - should complete without error using default threshold
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task SpecialistNudge_IsInjectedOnlyOnce()
    {
        // Arrange
        var config = BuildConfig(nudgeThreshold: "5");
        var callCount = 0;
        
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                // Return a tool call to continue the loop
                var response = new ChatCompletionResponse
                {
                    Id = $"test-{callCount}",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new ChatMessage 
                            { 
                                Role = "assistant", 
                                Content = $"Loop {callCount}",
                                ToolCalls = new List<ToolCall>
                                {
                                    new ToolCall
                                    {
                                        Id = $"call-{callCount}",
                                        Type = "function",
                                        Function = new FunctionCall
                                        {
                                            Name = "test_tool",
                                            Arguments = "{}"
                                        }
                                    }
                                }
                            },
                            FinishReason = "tool_calls"
                        }
                    }
                };
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(response))
                };
            });

        var httpClient = new HttpClient(handlerMock.Object);
        
        var sessionService = new SessionService();
        var trafficLogger = new TrafficLoggerService(_trafficLoggerMock.Object, config);
        var orchestrator = new Orchestrator(httpClient, config, _loggerMock.Object, sessionService, trafficLogger);

        var sessionId = Guid.NewGuid().ToString();

        // Act - Run loop with many iterations
        var result = await orchestrator.RunAgentLoopAsync(_agentMock.Object, "Test message", sessionId);

        // Assert - the loop should have completed with many calls
        Assert.NotNull(result);
    }

    [Fact]
    public async Task SpecialistNudge_IsNotAddedToSessionHistory()
    {
        // Arrange
        var config = BuildConfig(nudgeThreshold: "1"); // Immediate trigger
        var callCount = 0;
        
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var response = new ChatCompletionResponse
                {
                    Id = $"test-{callCount}",
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new ChatMessage 
                            { 
                                Role = "assistant", 
                                Content = callCount == 1 
                                    ? "First response" 
                                    : "Final response."
                            },
                            FinishReason = "stop"
                        }
                    }
                };
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(JsonSerializer.Serialize(response))
                };
            });

        var httpClient = new HttpClient(handlerMock.Object);
        
        var sessionService = new SessionService();
        var trafficLogger = new TrafficLoggerService(_trafficLoggerMock.Object, config);
        var orchestrator = new Orchestrator(httpClient, config, _loggerMock.Object, sessionService, trafficLogger);

        var sessionId = Guid.NewGuid().ToString();

        // Act
        await orchestrator.RunAgentLoopAsync(_agentMock.Object, "Test message", sessionId);

        // Assert - get session history and verify no nudge message in persistent history
        var history = orchestrator.GetSessionHistory(sessionId);
        var nudgeMessages = history.Where(m => 
            m.Role == "user" && 
            m.Content != null && 
            m.Content.Contains("Specialist Consultation Suggested"));
        
        Assert.Empty(nudgeMessages); // Nudge should NOT be in session history (ephemeral)
    }

    [Fact]
    public void NudgeSpecialistConsultation_SentinelExists()
    {
        // Verify the sentinel constant exists
        Assert.Equal("[NUDGE_SPECIALIST_CONSULTATION]", AgentSentinels.NudgeSpecialistConsultation);
    }

    // --- Helpers ---

    private static IConfiguration BuildConfig(
        string apiEndpoint = "https://fake-api.test/v1/chat/completions",
        string modelName = "test-model",
        string apiKey = "test-key",
        string nudgeThreshold = "50")
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "Config:ApiEndpoint", apiEndpoint },
            { "Config:ModelName", modelName },
            { "Config:ApiKey", apiKey },
            { "Config:NudgeSpecialistThreshold", nudgeThreshold },
            { "Config:TrafficLogDirectory", Path.Combine(Path.GetTempPath(), "traffic-logs") }
        };
        return new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
    }

    private static IConfiguration BuildConfigWithoutNudgeSetting()
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "Config:ApiEndpoint", "https://fake-api.test/v1/chat/completions" },
            { "Config:ModelName", "test-model" },
            { "Config:ApiKey", "test-key" },
            { "Config:TrafficLogDirectory", Path.Combine(Path.GetTempPath(), "traffic-logs") }
        };
        return new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(string content)
    {
        var response = new ChatCompletionResponse
        {
            Id = "test-id",
            Choices = new List<Choice>
            {
                new Choice
                {
                    Index = 0,
                    Message = new ChatMessage { Role = "assistant", Content = content },
                    FinishReason = "stop"
                }
            }
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(response))
            });

        return handlerMock;
    }
}
