using System.Text.Json;
using Xunit;
using Abo.Core.Models;

namespace Abo.Tests;

/// <summary>
/// Unit tests for ConsultationModels classes.
/// Part of the Consultation Message Protocol (Issue #406).
/// </summary>
[Trait("Category", "Unit")]
public class ConsultationModelsTests
{
    #region ConsultationRequest Tests

    [Fact]
    public void ConsultationRequest_HasCorrectDefaults()
    {
        // Arrange & Act
        var request = new ConsultationRequest();

        // Assert
        Assert.Equal(12, request.ConsultationId.Length);
        Assert.All(request.ConsultationId, c => Assert.True(char.IsAsciiHexDigit(c)));
        Assert.Equal(5, request.MaxTurns);
        Assert.Equal(string.Empty, request.CallerAgentName);
        Assert.Equal(string.Empty, request.TaskDescription);
        Assert.Equal(string.Empty, request.ContextSummary);
        Assert.Null(request.SpecialistDomain);
        Assert.Null(request.ParentSessionId);
        Assert.Null(request.IssueId);
        Assert.Null(request.TimeoutConfig);
        Assert.True((DateTime.UtcNow - request.RequestedAt).TotalSeconds < 1);
    }

    [Fact]
    public void ConsultationRequest_CanSetProperties()
    {
        // Arrange
        var consultationId = "abc123def456";
        var callerAgentName = "ManagerAgent";
        var specialistDomain = "architecture";
        var taskDescription = "Design a microservices architecture";
        var contextSummary = "Building a distributed system";
        var parentSessionId = "session-001";
        var issueId = "444";
        var requestedAt = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var maxTurns = 10;

        // Act
        var request = new ConsultationRequest
        {
            ConsultationId = consultationId,
            CallerAgentName = callerAgentName,
            SpecialistDomain = specialistDomain,
            TaskDescription = taskDescription,
            ContextSummary = contextSummary,
            ParentSessionId = parentSessionId,
            IssueId = issueId,
            RequestedAt = requestedAt,
            MaxTurns = maxTurns
        };

        // Assert
        Assert.Equal(consultationId, request.ConsultationId);
        Assert.Equal(callerAgentName, request.CallerAgentName);
        Assert.Equal(specialistDomain, request.SpecialistDomain);
        Assert.Equal(taskDescription, request.TaskDescription);
        Assert.Equal(contextSummary, request.ContextSummary);
        Assert.Equal(parentSessionId, request.ParentSessionId);
        Assert.Equal(issueId, request.IssueId);
        Assert.Equal(requestedAt, request.RequestedAt);
        Assert.Equal(maxTurns, request.MaxTurns);
    }

    [Fact]
    public void ConsultationRequest_GuidIsUnique()
    {
        // Arrange & Act
        var request1 = new ConsultationRequest();
        var request2 = new ConsultationRequest();

        // Assert
        Assert.NotEqual(request1.ConsultationId, request2.ConsultationId);
    }

    #endregion

    #region ConsultationMessage Tests

    [Fact]
    public void ConsultationMessage_HasCorrectDefaults()
    {
        // Arrange & Act
        var message = new ConsultationMessage();

        // Assert
        Assert.Equal(1, message.TurnNumber);
        Assert.Equal(string.Empty, message.Role);
        Assert.Equal(string.Empty, message.Content);
        Assert.Equal(string.Empty, message.ConsultationId);
        Assert.Null(message.Metadata);
        Assert.True((DateTime.UtcNow - message.Timestamp).TotalSeconds < 1);
    }

    [Fact]
    public void ConsultationMessage_CanSetAllProperties()
    {
        // Arrange
        var consultationId = "test12345678";
        var turnNumber = 3;
        var role = "specialist";
        var content = "Here is my recommendation...";
        var timestamp = new DateTime(2025, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var message = new ConsultationMessage
        {
            ConsultationId = consultationId,
            TurnNumber = turnNumber,
            Role = role,
            Content = content,
            Timestamp = timestamp
        };

        // Assert
        Assert.Equal(consultationId, message.ConsultationId);
        Assert.Equal(turnNumber, message.TurnNumber);
        Assert.Equal(role, message.Role);
        Assert.Equal(content, message.Content);
        Assert.Equal(timestamp, message.Timestamp);
    }

    #endregion

    #region StructuredRecommendation Tests

    [Fact]
    public void StructuredRecommendation_HasCorrectDefaults()
    {
        // Arrange & Act
        var recommendation = new StructuredRecommendation();

        // Assert
        Assert.Equal("medium", recommendation.Priority);
        Assert.Equal(string.Empty, recommendation.Category);
        Assert.Equal(string.Empty, recommendation.Description);
        Assert.Null(recommendation.ActionItems);
    }

    [Fact]
    public void StructuredRecommendation_ActionItems_CanBeSet()
    {
        // Arrange
        var recommendation = new StructuredRecommendation
        {
            Priority = "high",
            Category = "implementation",
            Description = "Use dependency injection",
            ActionItems = new List<string>
            {
                "Add DI container",
                "Register services",
                "Update configuration"
            }
        };

        // Assert
        Assert.Equal("high", recommendation.Priority);
        Assert.Equal("implementation", recommendation.Category);
        Assert.Equal("Use dependency injection", recommendation.Description);
        Assert.NotNull(recommendation.ActionItems);
        Assert.Equal(3, recommendation.ActionItems.Count);
        Assert.Contains("Add DI container", recommendation.ActionItems);
        Assert.Contains("Register services", recommendation.ActionItems);
        Assert.Contains("Update configuration", recommendation.ActionItems);
    }

    #endregion

    #region ConsultationTimeoutConfig Tests

    [Fact]
    public void ConsultationTimeoutConfig_HasCorrectDefaults()
    {
        // Arrange & Act
        var config = new ConsultationTimeoutConfig();

        // Assert
        Assert.Equal(60, config.TurnTimeoutSeconds);
        Assert.Equal(300, config.TotalTimeoutSeconds);
        Assert.True(config.AutoTerminateOnTimeout);
    }

    #endregion

    #region ConsultationResult Tests

    [Fact]
    public void ConsultationResult_HasCorrectDefaults()
    {
        // Arrange & Act
        var result = new ConsultationResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.SpecialistResponse);
        Assert.False(result.NeedsMoreInfo);
        Assert.Equal(string.Empty, result.TerminationReason);
        Assert.Equal(0, result.TotalInputTokens);
        Assert.Equal(0, result.TotalOutputTokens);
        Assert.Equal(0, result.TotalCost);
        Assert.Equal(string.Empty, result.ModelUsed);
        Assert.Null(result.InfoRequest);
        Assert.Null(result.Recommendations);
        Assert.Null(result.MessageHistory);
    }

    [Fact]
    public void ConsultationResult_SerializationDeserialization_Works()
    {
        // Arrange
        var recommendations = new List<StructuredRecommendation>
        {
            new()
            {
                Priority = "high",
                Category = "security",
                Description = "Add input validation",
                ActionItems = new List<string> { "Validate user input", "Sanitize data" }
            }
        };

        var original = new ConsultationResult
        {
            ConsultationId = "test12345678",
            Success = true,
            SpecialistResponse = "This is my analysis...",
            TurnsTaken = 3,
            TerminationReason = "Completed successfully",
            TotalInputTokens = 500,
            TotalOutputTokens = 200,
            TotalCost = 0.015,
            ModelUsed = "gpt-4",
            NeedsMoreInfo = false,
            Recommendations = recommendations
        };

        // Act
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ConsultationResult>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.ConsultationId, deserialized.ConsultationId);
        Assert.Equal(original.Success, deserialized.Success);
        Assert.Equal(original.SpecialistResponse, deserialized.SpecialistResponse);
        Assert.Equal(original.TurnsTaken, deserialized.TurnsTaken);
        Assert.Equal(original.TerminationReason, deserialized.TerminationReason);
        Assert.Equal(original.TotalInputTokens, deserialized.TotalInputTokens);
        Assert.Equal(original.TotalOutputTokens, deserialized.TotalOutputTokens);
        Assert.Equal(original.TotalCost, deserialized.TotalCost);
        Assert.Equal(original.ModelUsed, deserialized.ModelUsed);
        Assert.Equal(original.NeedsMoreInfo, deserialized.NeedsMoreInfo);

        // Verify recommendations were serialized correctly
        Assert.NotNull(deserialized.Recommendations);
        Assert.Single(deserialized.Recommendations);
        Assert.Equal("high", deserialized.Recommendations[0].Priority);
        Assert.Equal("security", deserialized.Recommendations[0].Category);
    }

    [Fact]
    public void ConsultationResult_MessageHistory_CanBePopulated()
    {
        // Arrange
        var messages = new List<ConsultationMessage>
        {
            new()
            {
                ConsultationId = "test123",
                TurnNumber = 1,
                Role = "caller",
                Content = "I need help with architecture"
            },
            new()
            {
                ConsultationId = "test123",
                TurnNumber = 1,
                Role = "specialist",
                Content = "What specific aspect?"
            }
        };

        // Act
        var result = new ConsultationResult
        {
            MessageHistory = messages
        };

        // Assert
        Assert.NotNull(result.MessageHistory);
        Assert.Equal(2, result.MessageHistory.Count);
        Assert.Equal("caller", result.MessageHistory[0].Role);
        Assert.Equal("specialist", result.MessageHistory[1].Role);
    }

    #endregion

    #region ConsultationSession Tests

    [Fact]
    public void ConsultationSession_InitialState_IsCorrect()
    {
        // Arrange & Act
        var session = new ConsultationSession();

        // Assert
        Assert.NotNull(session.Messages);
        Assert.Empty(session.Messages);
        Assert.Equal(ConsultationStatus.Active, session.Status);
        Assert.Equal(0, session.CurrentTurn);
        Assert.Equal(string.Empty, session.SessionId);
        Assert.Equal(string.Empty, session.SpecialistDomain);
        Assert.Equal(string.Empty, session.SpecialistModel);
        Assert.Equal(0, session.CallerFollowUpCount);
        Assert.Equal(0, session.SpecialistResponseCount);
        Assert.Null(session.LastActivityAt);
        Assert.Null(session.TerminationSignal);
        Assert.Null(session.EarlyTerminationTrigger);
    }

    #endregion

    #region ConsultationStatus Enum Tests

    [Fact]
    public void ConsultationStatus_AllExpectedValues_Exist()
    {
        // Assert
        Assert.True(Enum.IsDefined(typeof(ConsultationStatus), ConsultationStatus.Active));
        Assert.True(Enum.IsDefined(typeof(ConsultationStatus), ConsultationStatus.Completed));
        Assert.True(Enum.IsDefined(typeof(ConsultationStatus), ConsultationStatus.TimedOut));
        Assert.True(Enum.IsDefined(typeof(ConsultationStatus), ConsultationStatus.MaxTurnsReached));
        Assert.True(Enum.IsDefined(typeof(ConsultationStatus), ConsultationStatus.Error));
        Assert.True(Enum.IsDefined(typeof(ConsultationStatus), ConsultationStatus.Cancelled));

        // Verify default value
        Assert.Equal(0, (int)ConsultationStatus.Active);
    }

    #endregion

    #region EarlyTerminationTrigger Enum Tests

    [Fact]
    public void EarlyTerminationTrigger_AllExpectedValues_Exist()
    {
        // Assert
        Assert.True(Enum.IsDefined(typeof(EarlyTerminationTrigger), EarlyTerminationTrigger.CostThresholdExceeded));
        Assert.True(Enum.IsDefined(typeof(EarlyTerminationTrigger), EarlyTerminationTrigger.OutOfScope));
        Assert.True(Enum.IsDefined(typeof(EarlyTerminationTrigger), EarlyTerminationTrigger.InvalidResponse));
        Assert.True(Enum.IsDefined(typeof(EarlyTerminationTrigger), EarlyTerminationTrigger.ApiFailure));
        Assert.True(Enum.IsDefined(typeof(EarlyTerminationTrigger), EarlyTerminationTrigger.CancelledByCaller));

        // Verify count
        var values = Enum.GetValues<EarlyTerminationTrigger>();
        Assert.Equal(5, values.Length);
    }

    #endregion
}
