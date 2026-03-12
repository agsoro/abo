namespace Abo.Core.Connectors;

public interface IConnector
{
    Task<string> ReadFileAsync(string relativePath);
    Task<string> WriteFileAsync(string relativePath, string content);
    Task<string> DeleteFileAsync(string relativePath);
    Task<string> ListDirAsync(string relativePath);
    Task<string> MkDirAsync(string relativePath);
    Task<string> RunGitAsync(string arguments);
    Task<string> RunDotnetAsync(string arguments);
}
