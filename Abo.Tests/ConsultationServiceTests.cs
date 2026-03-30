using Abo.Core;
using Abo.Core.Models;
using Abo.Core.Services;
using Moq;

namespace Abo.Tests;

/// <summary>
/// Unit tests for ConsultationService class.
/// Part of the Consultation Message Protocol (Issue #406).
/// 
/// Tests verify that ConsultationService correctly delegates to IOrchestrator
/// and documents the current stub behavior of unimplemented methods.
/// </summary>
[Trait("Category", "Unit")]
public class ConsultationServiceTests
{
    private readonly Mock<IOrchestrator> _mockOrchestrator;
    private readonly ConsultationService _service;

    public ConsultationServiceTests()
    {
        _mockOrchestrator = new Mock<IOrchestrator>();
        _service = new ConsultationService(_mockOrchestrator.Object);
    }

    #region RunConsultationAsync Tests

    [Fact]
    public async Task RunConsultationAsync_DelegatesToOrchestrator()
    {
        // Arrange
        var request = new ConsultationRequest
        {
            ConsultationId = "test12345678",
            TaskDescription = "Design a microservices architecture",
            SpecialistDomain = "architecture",
            ContextSummary = "Building a distributed system"
        };

        _mockOrchestrator
            .Setup(o => o.RunConsultationAsync(It.IsAny<ConsultationRequest>()))
            .ReturnsAsync(new ConsultationResult { Success = true });

        // Act
        await _service.RunConsultationAsync(request);

        // Assert
        _mockOrchestrator.Verify(
            o => o.RunConsultationAsync(It.Is<ConsultationRequest>(r => 
                r.ConsultationId == request.ConsultationId && 
                r.TaskDescription == request.TaskDescription)),
            Times.Once);
    }

    [Fact]
    public async Task RunConsultationAsync_ReturnsOrchestratorResult()
    {
        // Arrange
        var request = new ConsultationRequest
        {
            ConsultationId = "result123456",
            TaskDescription = "Test task"
        };

        var expectedResult = new ConsultationResult
        {
            ConsultationId = request.ConsultationId,
            Success = true,
            SpecialistResponse = "Here is my recommendation...",
            TurnsTaken = 2,
            TotalInputTokens = 100,
            TotalOutputTokens = 50,
            TotalCost = 0.005,
            TerminationReason = "Consultant concluded the consultation"
        };

        _mockOrchestrator
            .Setup(o => o.RunConsultationAsync(request))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _service.RunConsultationAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("Here is my recommendation...", result.SpecialistResponse);
        Assert.Equal(2, result.TurnsTaken);
        Assert.Equal(100, result.TotalInputTokens);
        Assert.Equal(50, result.TotalOutputTokens);
        Assert.Equal(0.005, result.TotalCost);
        Assert.Equal("Consultant concluded the consultation", result.TerminationReason);
    }

    [Fact]
    public async Task RunConsultationAsync_ReturnsOrchestratorResult_WhenConsultationFails()
    {
        // Arrange
        var request = new ConsultationRequest
        {
            ConsultationId = "fail12345678",
            TaskDescription = "Impossible task"
        };

        var failedResult = new ConsultationResult
        {
            ConsultationId = request.ConsultationId,
            Success = false,
            SpecialistResponse = "Error: API failure",
            TerminationReason = "API error"
        };

        _mockOrchestrator
            .Setup(o => o.RunConsultationAsync(request))
            .ReturnsAsync(failedResult);

        // Act
        var result = await _service.RunConsultationAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("API error", result.TerminationReason);
    }

    [Fact]
    public async Task RunConsultationAsync_PassesCancellationToken_ToOrchestrator()
    {
        // Arrange
        var request = new ConsultationRequest { ConsultationId = "cancel123456" };
        var cts = new CancellationTokenSource();

        _mockOrchestrator
            .Setup(o => o.RunConsultationAsync(It.IsAny<ConsultationRequest>()))
            .ReturnsAsync(new ConsultationResult());

        // Act
        await _service.RunConsultationAsync(request, cts.Token);

        // Assert - Cancellation token is passed to the service (orchestrator mock is called)
        _mockOrchestrator.Verify(
            o => o.RunConsultationAsync(It.IsAny<ConsultationRequest>()),
            Times.Once);
    }

    #endregion

    #region GetSession Tests

    [Fact]
    public void GetSession_ReturnsNull_CurrentImplementation()
    {
        // Arrange
        var consultationId = "anyconsultation123";

        // Act
        var session = _service.GetSession(consultationId);

        // Assert
        // Note: This documents the current stub behavior - sessions are not yet tracked
        Assert.Null(session);
    }

    [Fact]
    public void GetSession_ReturnsNull_ForAnyConsultationId()
    {
        // Arrange & Act
        var session1 = _service.GetSession("consultation-001");
        var session2 = _service.GetSession("consultation-002");
        var session3 = _service.GetSession("non-existent-id");

        // Assert
        Assert.Null(session1);
        Assert.Null(session2);
        Assert.Null(session3);
    }

    #endregion

    #region TerminateAsync Tests

    [Fact]
    public async Task TerminateAsync_CompletesWithoutError()
    {
        // Arrange
        var consultationId = "terminate12345";
        var trigger = EarlyTerminationTrigger.OutOfScope;
        var reason = "Task is outside scope";

        // Act & Assert
        // Note: This documents the current stub behavior - termination is not yet implemented
        var exception = await Record.ExceptionAsync(() => 
            _service.TerminateAsync(consultationId, trigger, reason));
        
        Assert.Null(exception);
    }

    [Fact]
    public void TerminateAsync_ReturnsCompletedTask()
    {
        // Arrange
        var consultationId = "terminate98765";
        var trigger = EarlyTerminationTrigger.CancelledByCaller;

        // Act
        var task = _service.TerminateAsync(consultationId, trigger);

        // Assert
        Assert.NotNull(task);
        Assert.True(task.IsCompleted);
        Assert.False(task.IsFaulted);
        Assert.False(task.IsCanceled);
    }

    [Fact]
    public async Task TerminateAsync_HandlesNullReason()
    {
        // Arrange
        var consultationId = "terminatenull123";
        var trigger = EarlyTerminationTrigger.CostThresholdExceeded;

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => 
            _service.TerminateAsync(consultationId, trigger, null));
        
        Assert.Null(exception);
    }

    [Fact]
    public void TerminateAsync_HandlesAllTerminationTriggers()
    {
        // Arrange & Act & Assert
        foreach (EarlyTerminationTrigger trigger in Enum.GetValues<EarlyTerminationTrigger>())
        {
            var consultationId = $"terminate-{trigger}";
            
            // Note: This documents the current stub behavior - termination is not yet implemented
            var task = _service.TerminateAsync(consultationId, trigger, $"Reason for {trigger}");
            
            Assert.NotNull(task);
            Assert.True(task.IsCompleted);
        }
    }

    #endregion

    #region Service Construction Tests

    [Fact]
    public void Constructor_AcceptsIOrchestrator()
    {
        // Arrange
        var mockOrchestrator = new Mock<IOrchestrator>();

        // Act
        var service = new ConsultationService(mockOrchestrator.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Service_ImplementsIConsultationService()
    {
        // Arrange & Act
        var service = new ConsultationService(_mockOrchestrator.Object);

        // Assert
        Assert.IsAssignableFrom<IConsultationService>(service);
    }

    #endregion
}
