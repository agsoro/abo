namespace Abo.Contracts.Models;

/// <summary>
/// Defines the canonical allowed values for the issue 'type' field.
/// </summary>
public static class IssueType
{
    public const string Feature     = "feature";
    public const string Bug         = "bug";
    public const string Improvement = "improvement";
    public const string Task        = "task";
    public const string Chore       = "chore";
    public const string Doc         = "doc";

    public static readonly IReadOnlyList<string> AllowedValues = new[]
    {
        Feature, Bug, Improvement, Task, Chore, Doc
    };

    public static bool IsValid(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase);
}
