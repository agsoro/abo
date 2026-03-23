using System;
using System.Collections.Generic;
using Abo.Contracts.Models;

namespace Abo.Core;

public class WorkflowTransition
{
    public string ConditionName { get; set; } = string.Empty;
    public string NextStepId { get; set; } = string.Empty;
    public Action<IssueRecord>? ApplyState { get; set; }
}

public static class WorkflowEngine
{
    public static ProcessStepInfo? GetStepInfo(string stepId)
    {
        return stepId.ToLower() switch
        {
            "requested" => new ProcessStepInfo { StepId = "requested", StepName = "Triage Request", RequiredRole = "Role_Productmanager" },
            "invalid" => new ProcessStepInfo { StepId = "invalid", StepName = "Rejected or Duplicate", RequiredRole = "" },
            "planned" => new ProcessStepInfo { StepId = "planned", StepName = "Solution Planning", RequiredRole = "Role_Architect" },
            "work" => new ProcessStepInfo { StepId = "work", StepName = "Implementation", RequiredRole = "Role_Developer" },
            "review" => new ProcessStepInfo { StepId = "review", StepName = "QA Review", RequiredRole = "Role_QA" },
            "check" => new ProcessStepInfo { StepId = "check", StepName = "Release Documentation", RequiredRole = "Role_Productmanager" },
            "waiting customer" => new ProcessStepInfo { StepId = "waiting customer", StepName = "Waiting for Customer Input", RequiredRole = "Role_Productmanager" },
            "done" => new ProcessStepInfo { StepId = "done", StepName = "Completed", RequiredRole = "" },
            _ => null
        };
    }

    public static List<WorkflowTransition> GetTransitions(string stepId)
    {
        return stepId.ToLower() switch
        {
            "requested" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Reject or Duplicate?", NextStepId = "invalid", ApplyState = issue => SetProject(issue, null) },
                new WorkflowTransition { ConditionName = "Must-have?", NextStepId = "planned", ApplyState = issue => SetProject(issue, "release-current") },
                new WorkflowTransition { ConditionName = "Should-have?", NextStepId = "planned", ApplyState = issue => SetProject(issue, "release-next") },
                new WorkflowTransition { ConditionName = "Other / Default", NextStepId = "planned", ApplyState = issue => SetProject(issue, "planned") }
            },
            "planned" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Needs more input/help?", NextStepId = "waiting customer" },
                new WorkflowTransition { ConditionName = "Solution Planned successfully", NextStepId = "work" }
            },
            "work" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Needs more input/help?", NextStepId = "waiting customer" },
                new WorkflowTransition { ConditionName = "Implementation completed", NextStepId = "review" }
            },
            "review" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Should the solution be rejected?", NextStepId = "work" },
                new WorkflowTransition { ConditionName = "Solution Accepted", NextStepId = "check" }
            },
            "check" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Documentation and Release steps finished", NextStepId = "done" }
            },
            "waiting customer" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Feedback received, return to Planning", NextStepId = "planned" },
                new WorkflowTransition { ConditionName = "Feedback received, return to Work", NextStepId = "work" }
            },
            _ => new List<WorkflowTransition>()
        };
    }

    private static void SetProject(IssueRecord issue, string? newValue)
    {
        issue.Project = newValue ?? string.Empty;
    }
}
