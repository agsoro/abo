namespace Abo.Contracts.Models;

public class IssueRecord
{
    public string Id { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string State { get; set; } = "open";
    public string Project { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public List<string> Comments { get; set; } = new();
}
