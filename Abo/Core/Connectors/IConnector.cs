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
    Task<string> RunPythonAsync(string arguments);
    Task<string> SearchRegexAsync(string relativePath, string pattern, int limitLinesPerFile);

    /// <summary>
    /// Führt einen HTTP GET Request an die angegebene URL aus.
    /// </summary>
    /// <param name="url">Die vollständige URL (muss http:// oder https:// beginnen).</param>
    /// <param name="headers">Optionale HTTP-Header als Dictionary.</param>
    /// <param name="timeoutSeconds">Timeout in Sekunden (Standard: 30, Maximum: 120).</param>
    /// <returns>Formatierter String mit HTTP-Status und Response Body (max 100 KB).</returns>
    Task<string> HttpGetAsync(
        string url,
        Dictionary<string, string>? headers = null,
        int timeoutSeconds = 30
    );
}
