namespace Abo.Core.Connectors;

public interface IWikiConnector
{
    Task<string> GetPageAsync(string path);
    Task<string> CreatePageAsync(string title, string content, string? parentPath = null);
    Task<string> UpdatePageAsync(string path, string content);
    Task<string> SearchPagesAsync(string query);
    Task<string> MovePageAsync(string pathOrId, string newPathOrParentId, string? newTitle = null);

    /// <summary>
    /// Applies a unified diff/patch to a wiki page.
    /// </summary>
    /// <param name="path">Target wiki page path or ID.</param>
    /// <param name="patch">Unified diff format patch string.</param>
    Task<string> PatchPageAsync(string path, string patch);
}
