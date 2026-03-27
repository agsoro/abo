namespace Abo.Contracts.Models;

public class ProcessStepInfo
{
    public string StepId { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public RoleDefinition? Role { get; set; }
}
