using System.Diagnostics;

namespace Abo.Core.Connectors;

public class LocalWindowsConnector : IConnector
{
    private readonly ConnectorEnvironment _environment;

    public LocalWindowsConnector(ConnectorEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(environment.Dir))
        {
            throw new ArgumentException("Environment directory cannot be empty.");
        }

        // Ensure base directory exists
        if (!Directory.Exists(environment.Dir))
        {
            Directory.CreateDirectory(environment.Dir);
        }

        _environment = environment;
    }

    private string GetFullPath(string relativePath)
    {
        // Prevent directory traversal attacks
        var combinedPath = Path.GetFullPath(Path.Combine(_environment.Dir, relativePath));
        if (!combinedPath.StartsWith(Path.GetFullPath(_environment.Dir)))
        {
            throw new UnauthorizedAccessException("Cannot access paths outside the environment directory.");
        }
        return combinedPath;
    }

    public async Task<string> ReadFileAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        if (!File.Exists(path)) return $"Error: File '{relativePath}' not found.";
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    public async Task<string> WriteFileAsync(string relativePath, string content)
    {
        var path = GetFullPath(relativePath);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(path, content);
            return $"Successfully wrote to '{relativePath}'.";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    public Task<string> DeleteFileAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        if (!File.Exists(path)) return Task.FromResult($"Error: File '{relativePath}' not found.");
        try
        {
            File.Delete(path);
            return Task.FromResult($"Successfully deleted '{relativePath}'.");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error deleting file: {ex.Message}");
        }
    }

    public Task<string> ListDirAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        if (!Directory.Exists(path)) return Task.FromResult($"Error: Directory '{relativePath}' not found.");
        try
        {
            var directories = Directory.GetDirectories(path).Select(Path.GetFileName).Select(d => $"[DIR]  {d}");
            var files = Directory.GetFiles(path).Select(Path.GetFileName).Select(f => $"       {f}");

            var allItems = directories.Concat(files);
            return Task.FromResult(string.Join("\n", allItems));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error listing directory: {ex.Message}");
        }
    }

    public Task<string> MkDirAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                return Task.FromResult($"Successfully created directory '{relativePath}'.");
            }
            return Task.FromResult($"Directory '{relativePath}' already exists.");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error creating directory: {ex.Message}");
        }
    }

    public async Task<string> RunGitAsync(string arguments)
    {
        return await RunProcessAsync("git", arguments);
    }

    public async Task<string> RunDotnetAsync(string arguments)
    {
        return await RunProcessAsync("dotnet", arguments);
    }

    private async Task<string> RunProcessAsync(string command, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = _environment.Dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return $"Error ({process.ExitCode}): {error}\n{output}";
            }

            return string.IsNullOrWhiteSpace(output) ? "Command executed successfully with no output." : output;
        }
        catch (Exception ex)
        {
            return $"Failed to start process '{command}': {ex.Message}";
        }
    }
}
