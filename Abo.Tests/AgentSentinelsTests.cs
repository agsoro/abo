using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Abo.Core;

namespace Abo.Tests;

/// <summary>
/// Unit tests for AgentSentinels constants.
/// Verifies that all sentinel values are defined correctly and follow
/// the expected format conventions for the Consultation Message Protocol.
/// </summary>
[Trait("Category", "Unit")]
public class AgentSentinelsTests
{
    #region Consultation Protocol Sentinel Tests

    [Fact]
    public void ConsultationComplete_SentinelHasCorrectValue()
    {
        Assert.Equal("[CONSULTATION_COMPLETE]", AgentSentinels.ConsultationComplete);
    }

    [Fact]
    public void NeedsMoreInfo_SentinelHasCorrectValue()
    {
        Assert.Equal("[NEEDS_MORE_INFO]", AgentSentinels.NeedsMoreInfo);
    }

    [Fact]
    public void Conclusion_SentinelHasCorrectValue()
    {
        Assert.Equal("[CONCLUSION]", AgentSentinels.Conclusion);
    }

    [Fact]
    public void ConsultationTerminate_SentinelHasCorrectValue()
    {
        Assert.Equal("[CONSULTATION_TERMINATE]", AgentSentinels.ConsultationTerminate);
    }

    [Fact]
    public void OutOfScope_SentinelHasCorrectValue()
    {
        Assert.Equal("[OUT_OF_SCOPE]", AgentSentinels.OutOfScope);
    }

    [Fact]
    public void Timeout_SentinelHasCorrectValue()
    {
        Assert.Equal("[TIMEOUT]", AgentSentinels.Timeout);
    }

    [Fact]
    public void MaxTurns_SentinelHasCorrectValue()
    {
        Assert.Equal("[MAX_TURNS]", AgentSentinels.MaxTurns);
    }

    [Fact]
    public void NudgeSpecialistConsultation_SentinelHasCorrectValue()
    {
        Assert.Equal("[NUDGE_SPECIALIST_CONSULTATION]", AgentSentinels.NudgeSpecialistConsultation);
    }

    #endregion

    #region Lifecycle Sentinel Tests

    [Fact]
    public void ConcludeStepResult_SentinelHasCorrectValue()
    {
        Assert.Equal("[CONCLUDE_STEP_RESULT]:", AgentSentinels.ConcludeStepResult);
    }

    [Fact]
    public void PostponeTaskResult_SentinelHasCorrectValue()
    {
        Assert.Equal("[POSTPONE_TASK_RESULT]:", AgentSentinels.PostponeTaskResult);
    }

    #endregion

    #region Tool Result Sentinel Tests

    [Fact]
    public void SpecialistConsultationComplete_SentinelHasCorrectValue()
    {
        Assert.Equal("[SPECIALIST_CONSULTATION_COMPLETE]", AgentSentinels.SpecialistConsultationComplete);
    }

    [Fact]
    public void SpecialistNeedsMoreInfo_SentinelHasCorrectValue()
    {
        Assert.Equal("[SPECIALIST_NEEDS_MORE_INFO]", AgentSentinels.SpecialistNeedsMoreInfo);
    }

    #endregion

    #region Format Validation Tests

    [Fact]
    public void ConsultationProtocolSentinelsFollowBracketNotation()
    {
        // Consultation protocol sentinels should start with '[' and end with ']'
        var sentinels = new[]
        {
            AgentSentinels.ConsultationComplete,
            AgentSentinels.NeedsMoreInfo,
            AgentSentinels.Conclusion,
            AgentSentinels.ConsultationTerminate,
            AgentSentinels.OutOfScope,
            AgentSentinels.Timeout,
            AgentSentinels.MaxTurns,
            AgentSentinels.NudgeSpecialistConsultation
        };

        foreach (var sentinel in sentinels)
        {
            Assert.StartsWith("[", sentinel);
            Assert.EndsWith("]", sentinel);
        }
    }

    [Fact]
    public void ToolResultSentinelsFollowBracketNotation()
    {
        // Tool result sentinels should start with '[' and end with ']'
        var sentinels = new[]
        {
            AgentSentinels.SpecialistConsultationComplete,
            AgentSentinels.SpecialistNeedsMoreInfo
        };

        foreach (var sentinel in sentinels)
        {
            Assert.StartsWith("[", sentinel);
            Assert.EndsWith("]", sentinel);
        }
    }

    [Fact]
    public void LifecycleSentinelsFollowBracketNotationWithColon()
    {
        // Lifecycle sentinels should start with '[' and end with ':'
        var sentinels = new[]
        {
            AgentSentinels.ConcludeStepResult,
            AgentSentinels.PostponeTaskResult
        };

        foreach (var sentinel in sentinels)
        {
            Assert.StartsWith("[", sentinel);
            Assert.EndsWith(":", sentinel);
        }
    }

    [Fact]
    public void LifecycleSentinelsEndWithColon()
    {
        // Lifecycle sentinels should end with colon delimiter
        Assert.EndsWith(":", AgentSentinels.ConcludeStepResult);
        Assert.EndsWith(":", AgentSentinels.PostponeTaskResult);
    }

    [Fact]
    public void ConsultationSentinelsDoNotEndWithColon()
    {
        // Consultation protocol sentinels should NOT end with colon
        Assert.False(AgentSentinels.ConsultationComplete.EndsWith(":"));
        Assert.False(AgentSentinels.NeedsMoreInfo.EndsWith(":"));
        Assert.False(AgentSentinels.Conclusion.EndsWith(":"));
        Assert.False(AgentSentinels.ConsultationTerminate.EndsWith(":"));
        Assert.False(AgentSentinels.OutOfScope.EndsWith(":"));
        Assert.False(AgentSentinels.Timeout.EndsWith(":"));
        Assert.False(AgentSentinels.MaxTurns.EndsWith(":"));
        Assert.False(AgentSentinels.NudgeSpecialistConsultation.EndsWith(":"));
    }

    [Fact]
    public void ToolResultSentinelsDoNotEndWithColon()
    {
        // Tool result sentinels should NOT end with colon
        Assert.False(AgentSentinels.SpecialistConsultationComplete.EndsWith(":"));
        Assert.False(AgentSentinels.SpecialistNeedsMoreInfo.EndsWith(":"));
    }

    #endregion
}
