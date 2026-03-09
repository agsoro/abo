using Abo.Agents;
using Abo.Contracts.OpenAI;
using Abo.Tools;
using Moq;

namespace Abo.Tests;

public class QuizAgentTests
{
    private readonly List<Mock<IAboTool>> _mockTools;
    private readonly QuizAgent _agent;

    public QuizAgentTests()
    {
        _mockTools = CreateMockQuizTools();
        _agent = new QuizAgent(_mockTools.Select(m => m.Object));
    }

    [Fact]
    public void Name_ReturnsQuizAgent()
    {
        Assert.Equal("QuizAgent", _agent.Name);
    }

    [Fact]
    public void GetToolDefinitions_ReturnsOnlyQuizRelatedTools()
    {
        var definitions = _agent.GetToolDefinitions();

        // Should only include tools in the QuizAgent's whitelist
        var expectedNames = new[] { "subscribe_quiz", "unsubscribe_quiz", "get_quiz_leaderboard", "update_quiz_score", "ask_quiz_question", "ask_multiple_choice", "get_system_time" };
        Assert.All(definitions, d => Assert.Contains(d.Function.Name, expectedNames));
    }

    [Fact]
    public void GetToolDefinitions_ExcludesNonQuizTools()
    {
        // Add an extra non-quiz tool
        var extraTool = CreateMockTool("some_other_tool", "Does something else");
        var allTools = _mockTools.Select(m => m.Object).Append(extraTool.Object);
        var agent = new QuizAgent(allTools);

        var definitions = agent.GetToolDefinitions();

        Assert.DoesNotContain(definitions, d => d.Function.Name == "some_other_tool");
    }

    [Fact]
    public void GetToolDefinitions_SetsTypeToFunction()
    {
        var definitions = _agent.GetToolDefinitions();

        Assert.All(definitions, d => Assert.Equal("function", d.Type));
    }

    [Fact]
    public async Task HandleToolCallAsync_RoutesToCorrectTool()
    {
        var expectedResult = "Quiz question asked!";
        var askQuizMock = _mockTools.First(m => m.Object.Name == "ask_quiz_question");
        askQuizMock.Setup(t => t.ExecuteAsync(It.IsAny<string>())).ReturnsAsync(expectedResult);

        var toolCall = new ToolCall
        {
            Id = "call_1",
            Function = new FunctionCall { Name = "ask_quiz_question", Arguments = "{}" }
        };

        var result = await _agent.HandleToolCallAsync(toolCall);

        Assert.Equal(expectedResult, result);
        askQuizMock.Verify(t => t.ExecuteAsync("{}"), Times.Once);
    }

    [Fact]
    public async Task HandleToolCallAsync_ReturnsErrorForUnknownTool()
    {
        var toolCall = new ToolCall
        {
            Id = "call_2",
            Function = new FunctionCall { Name = "nonexistent_tool", Arguments = "{}" }
        };

        var result = await _agent.HandleToolCallAsync(toolCall);

        Assert.Contains("Error", result);
        Assert.Contains("nonexistent_tool", result);
    }

    [Fact]
    public async Task HandleToolCallAsync_PassesArgumentsToTool()
    {
        var args = "{\"channel_id\": \"abc123\"}";
        var subscribeMock = _mockTools.First(m => m.Object.Name == "subscribe_quiz");
        subscribeMock.Setup(t => t.ExecuteAsync(args)).ReturnsAsync("Subscribed!");

        var toolCall = new ToolCall
        {
            Id = "call_3",
            Function = new FunctionCall { Name = "subscribe_quiz", Arguments = args }
        };

        var result = await _agent.HandleToolCallAsync(toolCall);

        subscribeMock.Verify(t => t.ExecuteAsync(args), Times.Once);
        Assert.Equal("Subscribed!", result);
    }

    // --- Helpers ---

    private static List<Mock<IAboTool>> CreateMockQuizTools()
    {
        var toolNames = new[] { "subscribe_quiz", "unsubscribe_quiz", "get_quiz_leaderboard", "update_quiz_score", "ask_quiz_question", "ask_multiple_choice", "get_system_time" };
        return toolNames.Select(name => CreateMockTool(name, $"Description for {name}")).ToList();
    }

    private static Mock<IAboTool> CreateMockTool(string name, string description)
    {
        var mock = new Mock<IAboTool>();
        mock.Setup(t => t.Name).Returns(name);
        mock.Setup(t => t.Description).Returns(description);
        mock.Setup(t => t.ParametersSchema).Returns(new { type = "object" });
        return mock;
    }
}
