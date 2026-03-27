using System;

namespace Abo.Contracts.Models;

public class WorkflowTransition
{
    public string NextStatus { get; set; } = string.Empty;
    public bool IsEndEvent { get; set; }
    public Action<IssueRecord>? ApplyState { get; set; }
}
