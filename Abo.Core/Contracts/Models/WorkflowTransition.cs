using System;

namespace Abo.Contracts.Models;

public class WorkflowTransition
{
    public string ConditionName { get; set; } = string.Empty;
    public string NextStepId { get; set; } = string.Empty;
    public Action<IssueRecord>? ApplyState { get; set; }
}
