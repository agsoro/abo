namespace Abo.Core.Models;

public class IssueConsumptionRecord
{
    public string IssueId { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
    public double TotalCost { get; set; }
}
