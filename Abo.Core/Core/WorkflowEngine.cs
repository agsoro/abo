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
    public static string ResolveStepIdFallback(IssueRecord issue)
    {
        var stepId = issue.StepId ?? string.Empty;

        // If the step is recognized natively, accept it immediately
        if (!string.IsNullOrWhiteSpace(stepId) && GetStepInfo(stepId) != null) return stepId;

        // If the step is blank OR unrecognized (e.g. 'Open', 'Todo') but within the 'requested' project, force it
        if (string.Equals(issue.Project, "requested", StringComparison.OrdinalIgnoreCase)) return "open";

        return stepId;
    }

    public static ProcessStepInfo? GetStepInfo(string stepId)
    {
        return stepId.ToLower() switch
        {
            "open" => new ProcessStepInfo { StepId = "open", StepName = "Triage Request", RequiredRole = "Role_Productmanager" },
            "planned" => new ProcessStepInfo { StepId = "planned", StepName = "Solution Planning", RequiredRole = "Role_Architect" },
            "work" => new ProcessStepInfo { StepId = "work", StepName = "Implementation", RequiredRole = "Role_Developer" },
            "review" => new ProcessStepInfo { StepId = "review", StepName = "QA Review", RequiredRole = "Role_QA" },
            "check" => new ProcessStepInfo { StepId = "check", StepName = "Release", RequiredRole = "Role_Releaseengineer" },
            "done" => new ProcessStepInfo { StepId = "done", StepName = "Completed", RequiredRole = "" },
            "invalid" => new ProcessStepInfo { StepId = "invalid", StepName = "Rejected or Duplicate", RequiredRole = "" },
            "waiting customer" => new ProcessStepInfo { StepId = "waiting customer", StepName = "Waiting for Customer Input", RequiredRole = "" },
            _ => null
        };
    }

    public static List<WorkflowTransition> GetTransitions(string stepId)
    {
        return stepId.ToLower() switch
        {
            "open" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Reject or Duplicate", NextStepId = "invalid", ApplyState = issue => SetProject(issue, "requested") },
                new WorkflowTransition { ConditionName = "Must-have", NextStepId = "planned", ApplyState = issue => SetProject(issue, "release-current") },
                new WorkflowTransition { ConditionName = "Should-have", NextStepId = "planned", ApplyState = issue => SetProject(issue, "release-next") },
                new WorkflowTransition { ConditionName = "Backlog", NextStepId = "planned", ApplyState = issue => SetProject(issue, "planned") }
            },
            "planned" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Problem: Need more input/help", NextStepId = "waiting customer" },
                new WorkflowTransition { ConditionName = "Solution planned", NextStepId = "work" }
            },
            "work" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Problem: Need more input/help", NextStepId = "waiting customer" },
                new WorkflowTransition { ConditionName = "Implementation completed", NextStepId = "review" }
            },
            "review" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Problem: Solution rejected", NextStepId = "planned" },
                new WorkflowTransition { ConditionName = "Solution accepted", NextStepId = "check" }
            },
            "check" => new List<WorkflowTransition>
            {
                new WorkflowTransition { ConditionName = "Release finished", NextStepId = "done" }
            },
            _ => new List<WorkflowTransition>()
        };
    }

    private static void SetProject(IssueRecord issue, string? newValue)
    {
        issue.Project = newValue ?? string.Empty;
    }
}
