namespace Abo.Core.Connectors;

/// <summary>
/// Summary information about a wiki page for listing purposes.
/// </summary>
public record WikiPageSummary(
    string Path,
    string Title,
    DateTime? LastModified,
    string? ParentPath = null
);
