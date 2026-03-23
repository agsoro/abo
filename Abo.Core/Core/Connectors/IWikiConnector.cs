namespace Abo.Core.Connectors;

public interface IWikiConnector
{
    Task<string> GetPageAsync(string path);
    Task<string> CreatePageAsync(string title, string content, string? parentPath = null);
    Task<string> UpdatePageAsync(string path, string content);
    Task<string> SearchPagesAsync(string query);
    Task<string> MovePageAsync(string pathOrId, string newPathOrParentId, string? newTitle = null);
}
