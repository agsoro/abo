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

    /// <summary>
    /// Lists all wiki pages in the wiki, optionally within a parent directory.
    /// </summary>
    /// <param name="parentPath">Optional parent path to filter pages by directory.</param>
    /// <returns>A collection of wiki page summaries.</returns>
    Task<IEnumerable<WikiPageSummary>> ListPagesAsync(string? parentPath = null);

    /// <summary>
    /// Lists the wiki content as a tree structure starting from the specified path.
    /// </summary>
    /// <param name="path">Relative path within the wiki (use empty or "." for root).</param>
    /// <returns>Formatted tree view string showing files and directories.</returns>
    Task<string> ListWikiAsync(string path);
}
