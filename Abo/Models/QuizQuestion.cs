namespace Abo.Models;

public class QuizQuestion
{
    public string Id { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public Dictionary<string, string> Options { get; set; } = new();
    public string Answer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string? ExplanationUrl { get; set; }
}
