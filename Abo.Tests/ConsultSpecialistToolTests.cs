using Xunit;
using Moq;
using Abo.Agents;
using Abo.Core.Models;
using Abo.Core.Services;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the ConsultSpecialistTool.
/// </summary>
[Trait("Category", "Unit")]
public class ConsultSpecialistToolTests
{
    private readonly Mock<IConsultationService> _mockConsultationService;
    private readonly ConsultSpecialistTool _tool;

    public ConsultSpecialistToolTests()
    {
        _mockConsultationService = new Mock<IConsultationService>();
        _tool = new ConsultSpecialistTool(_mockConsultationService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidParameters_ReturnsConsultationResult()
    {
        // Arrange
        var consultationResult = new ConsultationResult
        {
            SpecialistResponse = "Test response",
            Success = true
        };

        _mockConsultationService
            .Setup(s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(consultationResult);

        var json = @"{""taskDescription"":""Test task"",""contextSummary"":""Test context""}";

        // Act
        var result = await _tool.ExecuteAsync(json);

        // Assert
        Assert.Contains("[SPECIALIST_CONSULTATION_COMPLETE]", result);
        Assert.Contains("Test response", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingTaskDescription_ReturnsError()
    {
        // Arrange
        var json = @"{""contextSummary"":""Test context""}";

        // Act
        var result = await _tool.ExecuteAsync(json);

        // Assert
        Assert.Contains("[ERROR]", result);
        Assert.Contains("Task description", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingContextSummary_ReturnsError()
    {
        // Arrange
        var json = @"{""taskDescription"":""Test task""}";

        // Act
        var result = await _tool.ExecuteAsync(json);

        // Assert
        Assert.Contains("[ERROR]", result);
        Assert.Contains("Context summary", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidJSON_ReturnsError()
    {
        // Arrange
        var json = "not valid json";

        // Act
        var result = await _tool.ExecuteAsync(json);

        // Assert
        Assert.Contains("[ERROR]", result);
        Assert.Contains("Invalid JSON", result);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyParameters_ReturnsError()
    {
        // Arrange
        var json = @"{""taskDescription"":"""",""contextSummary"":""""}";

        // Act
        var result = await _tool.ExecuteAsync(json);

        // Assert
        Assert.Contains("[ERROR]", result);
        // The tool should reject empty taskDescription or contextSummary
        Assert.True(
            result.Contains("Task description is required") || 
            result.Contains("Context summary is required"),
            $"Expected error about missing required parameter, but got: {result}");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullParameters_ReturnsError()
    {
        // Arrange
        _mockConsultationService
            .Setup(s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsultationResult)null!);

        var json = @"{""taskDescription"":""Test task"",""contextSummary"":""Test context""}";

        // Act
        var result = await _tool.ExecuteAsync(json);

        // Assert
        Assert.Contains("[ERROR]", result);
        Assert.Contains("failed to produce a result", result);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSpecialistNeedsMoreInfo_ReturnsNeedsMoreInfoSignal()
    {
        // Arrange
        var consultationResult = new ConsultationResult
        {
            NeedsMoreInfo = true,
            InfoRequest = "Need clarification",
            SpecialistResponse = "Partial response",
            Success = true
        };

        _mockConsultationService
            .Setup(s => s.RunConsultationAsync(It.IsAny<ConsultationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(consultationResult);

        var json = @"{""taskDescription"":""Test task"",""contextSummary"":""Test context""}";

        // Act
        var result = await _tool.ExecuteAsync(json);

        // Assert
        Assert.Contains("[SPECIALIST_NEEDS_MORE_INFO]", result);
        Assert.Contains("Need clarification", result);
        Assert.Contains("Partial response", result);
    }

    [Fact]
    public void Tool_Name_IsCorrect()
    {
        // Arrange & Act & Assert
        Assert.Equal("consult_specialist", _tool.Name);
    }

    [Fact]
    public void Tool_Description_IsCorrect()
    {
        // Arrange & Act & Assert
        Assert.Contains("Consult", _tool.Description);
        Assert.Contains("specialist", _tool.Description);
    }
}
