namespace Abo.Core.Connectors;

public interface IWorkspaceConnector
{
    Task<string> ReadFileAsync(string relativePath, bool important = false);
    Task<string> WriteFileAsync(string relativePath, string content);
    Task<string> DeleteFileAsync(string relativePath);
    Task<string> ListDirAsync(string relativePath);
    Task<string> MkDirAsync(string relativePath);
    Task<string> RunGitAsync(string arguments);
    Task<string> RunDotnetAsync(string arguments);
    Task<string> RunPythonAsync(string arguments);

    /// <summary>
    /// Runs a shell command (a named executable) in the workspace directory.
    /// Use for pyenv commands, shell scripts, and other tools not covered by git/dotnet/python.
    /// </summary>
    /// <param name="command">The executable/command name (e.g., "pyenv", "bash", "npm").</param>
    /// <param name="arguments">Arguments to pass to the command.</param>
    Task<string> RunShellAsync(string command, string arguments);

    Task<string> SearchRegexAsync(string relativePath, string pattern, int limitLinesPerFile);

    /// <summary>
    /// Applies a unified diff/patch to a file.
    /// </summary>
    /// <param name="relativePath">Target file path (relative to workspace).</param>
    /// <param name="patch">Unified diff format patch string.</param>
    /// <returns>Success message or descriptive error.</returns>
    Task<string> PatchFileAsync(string relativePath, string patch);

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
