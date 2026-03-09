using System.Net;
using System.Text.Json;
using Abo.Agents;
using Abo.Contracts.OpenAI;
using Abo.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Abo.Tests;

public class AgentSupervisorTests
{
    private readonly Mock<IAgent> _quizAgentMock;
    private readonly Mock<IAgent> _helloWorldAgentMock;
    private readonly List<IAgent> _agents;
    private readonly Mock<ILogger<AgentSupervisor>> _loggerMock;

    public AgentSupervisorTests()
    {
        _quizAgentMock = new Mock<IAgent>();
        _quizAgentMock.Setup(a => a.Name).Returns("QuizAgent");
        _quizAgentMock.Setup(a => a.Description).Returns("Handles quizzes");

        _helloWorldAgentMock = new Mock<IAgent>();
        _helloWorldAgentMock.Setup(a => a.Name).Returns("HelloWorldAgent");
        _helloWorldAgentMock.Setup(a => a.Description).Returns("Handles greetings");

        _agents = new List<IAgent> { _helloWorldAgentMock.Object, _quizAgentMock.Object };
        _loggerMock = new Mock<ILogger<AgentSupervisor>>();
    }

    [Fact]
    public async Task GetBestAgentAsync_FallsBackToFirstAgent_WhenConfigMissing()
    {
        var config = BuildConfig(apiEndpoint: "", modelName: "");
        var supervisor = CreateSupervisor(config);

        var result = await supervisor.GetBestAgentAsync("hello");

        Assert.Equal("HelloWorldAgent", result.Name);
    }

    [Fact]
    public async Task GetBestAgentAsync_ReturnsQuizAgent_WhenApiSelectsIt()
    {
        var config = BuildConfig();
        var handler = CreateMockHandler("QuizAgent");
        var supervisor = CreateSupervisor(config, handler);

        var result = await supervisor.GetBestAgentAsync("give me a trivia question");

        Assert.Equal("QuizAgent", result.Name);
    }

    [Fact]
    public async Task GetBestAgentAsync_ReturnsHelloWorldAgent_WhenApiSelectsIt()
    {
        var config = BuildConfig();
        var handler = CreateMockHandler("HelloWorldAgent");
        var supervisor = CreateSupervisor(config, handler);

        var result = await supervisor.GetBestAgentAsync("hello there!");

        Assert.Equal("HelloWorldAgent", result.Name);
    }

    [Fact]
    public async Task GetBestAgentAsync_FallsBackToHelloWorld_WhenApiReturnsUnknownAgent()
    {
        var config = BuildConfig();
        var handler = CreateMockHandler("NonExistentAgent");
        var supervisor = CreateSupervisor(config, handler);

        var result = await supervisor.GetBestAgentAsync("something random");

        Assert.Equal("HelloWorldAgent", result.Name);
    }

    [Fact]
    public async Task GetBestAgentAsync_FallsBackToHelloWorld_WhenApiCallFails()
    {
        var config = BuildConfig();
        var handler = CreateMockHandler("", HttpStatusCode.InternalServerError);
        var supervisor = CreateSupervisor(config, handler);

        var result = await supervisor.GetBestAgentAsync("test message");

        Assert.Equal("HelloWorldAgent", result.Name);
    }

    [Fact]
    public async Task GetBestAgentAsync_FallsBackToHelloWorld_WhenHttpThrows()
    {
        var config = BuildConfig();
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var supervisor = CreateSupervisor(config, handlerMock);

        var result = await supervisor.GetBestAgentAsync("test");

        Assert.Equal("HelloWorldAgent", result.Name);
    }

    [Fact]
    public async Task GetBestAgentAsync_IsCaseInsensitive_WhenMatchingAgentName()
    {
        var config = BuildConfig();
        var handler = CreateMockHandler("quizagent"); // lowercase
        var supervisor = CreateSupervisor(config, handler);

        var result = await supervisor.GetBestAgentAsync("quiz time");

        Assert.Equal("QuizAgent", result.Name);
    }

    // --- Helpers ---

    private static IConfiguration BuildConfig(string apiEndpoint = "https://fake-api.test/v1/chat/completions", string modelName = "test-model", string apiKey = "test-key")
    {
        var inMemory = new Dictionary<string, string?>
        {
            { "Config:ApiEndpoint", apiEndpoint },
            { "Config:ModelName", modelName },
            { "Config:ApiKey", apiKey }
        };
        return new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(string agentNameResponse, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new ChatCompletionResponse
        {
            Id = "test-id",
            Choices = new List<Choice>
            {
                new Choice
                {
                    Index = 0,
                    Message = new ChatMessage { Role = "assistant", Content = agentNameResponse },
                    FinishReason = "stop"
                }
            }
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(JsonSerializer.Serialize(response))
            });

        return handlerMock;
    }

    private AgentSupervisor CreateSupervisor(IConfiguration config, Mock<HttpMessageHandler>? handlerMock = null)
    {
        var handler = handlerMock ?? new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handler.Object);
        return new AgentSupervisor(_agents, httpClient, config, _loggerMock.Object);
    }
}
