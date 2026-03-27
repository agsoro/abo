using System.Collections.Generic;

namespace Abo.Contracts.Models;

public class ProcessStepInfo
{
    public string Status { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public RoleDefinition? Role { get; set; }
    public Dictionary<string, WorkflowTransition> Transitions { get; set; } = new();
}
