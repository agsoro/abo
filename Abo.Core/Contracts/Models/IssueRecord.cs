namespace Abo.Contracts.Models;

public class IssueRecord
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string State { get; set; } = "open";
    public List<string> Labels { get; set; } = new();
}
