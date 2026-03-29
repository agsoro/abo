using Abo.Agents;
using Abo.Core.Models;
using Abo.Core.Services;
using Abo.Tools;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Abo.Tests;

public class SpecialistAgentConsultToolTests
{
    private readonly Mock<IConsultationService> _mockConsultationService;
    private readonly IConfiguration _configuration;
    private readonly List<IAboTool> _globalTools;

    public SpecialistAgentConsultToolTests()
    {
        _mockConsultationService = new Mock<IConsultationService>();
        _configuration = new ConfigurationBuilder().Build();
        _globalTools = new List<IAboTool>();
    }

    [Fact]
    public void GetToolDefinitions_ShouldIncludeConsultSpecialistTool()
    {
        // Arrange
        var specialist = new SpecialistAgent(
            _globalTools,
            "Developer",
            _mockConsultationService.Object,
            "You are a developer.",
            new List<string>(),
            _configuration,
            "TEST-123");

        // Act
        var toolDefinitions = specialist.GetToolDefinitions();

        // Assert
        var consultTool = toolDefinitions.FirstOrDefault(
            t => t.Function?.Name == "consult_specialist");
        
        Assert.NotNull(consultTool);
        Assert.Equal("consult_specialist", consultTool.Function?.Name);
        Assert.Contains("Consult an expert specialist", consultTool.Function?.Description);
    }

    [Fact]
    public void GetToolDefinitions_ShouldIncludeConcludeStepTool()
    {
        // Arrange
        var specialist = new SpecialistAgent(
            _globalTools,
            "Developer",
            _mockConsultationService.Object,
            "You are a developer.",
            new List<string>(),
            _configuration,
            "TEST-123");

        // Act
        var toolDefinitions = specialist.GetToolDefinitions();

        // Assert
        var concludeTool = toolDefinitions.FirstOrDefault(
            t => t.Function?.Name == "conclude_step");
        
        Assert.NotNull(concludeTool);
        Assert.Equal("conclude_step", concludeTool.Function?.Name);
    }

    [Fact]
    public void GetToolDefinitions_ShouldHaveTwoTools_WhenNoWorkspaceInitialized()
    {
        // Arrange
        var specialist = new SpecialistAgent(
            _globalTools,
            "Developer",
            _mockConsultationService.Object,
            "You are a developer.",
            new List<string>(),
            _configuration,
            "TEST-123");

        // Act
        var toolDefinitions = specialist.GetToolDefinitions();

        // Assert - should have conclude_step + consult_specialist (no workspace tools)
        Assert.Equal(2, toolDefinitions.Count);
        Assert.Contains(toolDefinitions, t => t.Function?.Name == "conclude_step");
        Assert.Contains(toolDefinitions, t => t.Function?.Name == "consult_specialist");
    }

    [Fact]
    public async Task HandleToolCallAsync_ShouldExecuteConsultSpecialistTool()
    {
        // Arrange
        _mockConsultationService
            .Setup(s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationResult
            {
                SpecialistResponse = "Test consultation result"
            });

        var specialist = new SpecialistAgent(
            _globalTools,
            "Developer",
            _mockConsultationService.Object,
            "You are a developer.",
            new List<string>(),
            _configuration,
            "TEST-123");

        var toolCall = new Contracts.OpenAI.ToolCall
        {
            Function = new Contracts.OpenAI.FunctionCall
            {
                Name = "consult_specialist",
                Arguments = "{\"taskDescription\":\"Test task\",\"contextSummary\":\"Test context\"}"
            }
        };

        // Act
        var result = await specialist.HandleToolCallAsync(toolCall);

        // Assert
        Assert.Contains("SPECIALIST_CONSULTATION_COMPLETE", result);
        Assert.Contains("Test consultation result", result);
        _mockConsultationService.Verify(
            s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleToolCallAsync_ShouldRestrictConsultSpecialist_WhenNotInAllowedTools()
    {
        // Arrange
        var specialist = new SpecialistAgent(
            _globalTools,
            "Developer",
            _mockConsultationService.Object,
            "You are a developer.",
            new List<string> { "read_file", "write_file" }, // consult_specialist not in allowed list
            _configuration,
            "TEST-123");

        var toolCall = new Contracts.OpenAI.ToolCall
        {
            Function = new Contracts.OpenAI.FunctionCall
            {
                Name = "consult_specialist",
                Arguments = "{\"taskDescription\":\"Test task\",\"contextSummary\":\"Test context\"}"
            }
        };

        // Act
        var result = await specialist.HandleToolCallAsync(toolCall);

        // Assert
        Assert.Contains("restricted", result);
        Assert.Contains("cannot be run", result);
        _mockConsultationService.Verify(
            s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleToolCallAsync_ShouldAllowConsultSpecialist_WhenInAllowedTools()
    {
        // Arrange
        _mockConsultationService
            .Setup(s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationResult
            {
                SpecialistResponse = "Test result"
            });

        var specialist = new SpecialistAgent(
            _globalTools,
            "Developer",
            _mockConsultationService.Object,
            "You are a developer.",
            new List<string> { "consult_specialist", "read_file" }, // consult_specialist in allowed list
            _configuration,
            "TEST-123");

        var toolCall = new Contracts.OpenAI.ToolCall
        {
            Function = new Contracts.OpenAI.FunctionCall
            {
                Name = "consult_specialist",
                Arguments = "{\"taskDescription\":\"Test task\",\"contextSummary\":\"Test context\"}"
            }
        };

        // Act
        var result = await specialist.HandleToolCallAsync(toolCall);

        // Assert
        Assert.Contains("SPECIALIST_CONSULTATION_COMPLETE", result);
        _mockConsultationService.Verify(
            s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
