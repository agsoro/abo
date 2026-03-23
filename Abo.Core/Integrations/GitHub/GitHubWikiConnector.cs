using System.Diagnostics;
using System.Text.RegularExpressions;
using Abo.Core.Connectors;

namespace Abo.Integrations.GitHub;

public class GitHubWikiConnector : IWikiConnector
{
    private readonly string _cloneDir;
    private readonly string _repoUrl;
    private readonly string _token;

    public GitHubWikiConnector(ConnectorEnvironment environment, string token, string owner, string repo)
    {
        _token = token;
        _repoUrl = $"https://x-access-token:{token}@github.com/{owner}/{repo}.wiki.git";
        _cloneDir = Path.GetFullPath(Path.Combine(environment.Dir, ".github-wiki"));
    }

    private async Task SyncWikiAsync()
    {
        if (!Directory.Exists(_cloneDir) || !Directory.Exists(Path.Combine(_cloneDir, ".git")))
        {
            if (Directory.Exists(_cloneDir)) Directory.Delete(_cloneDir, true);
            await RunGitCommandAsync(Path.GetDirectoryName(_cloneDir)!, "clone", _repoUrl, _cloneDir);
        }
        else
        {
            await RunGitCommandAsync(_cloneDir, "pull", "origin", "master");
        }
    }

    private async Task CommitAndPushAsync(string message)
    {
        await RunGitCommandAsync(_cloneDir, "add", ".");
        await RunGitCommandAsync(_cloneDir, "commit", "-m", $"\"{message}\"");
        await RunGitCommandAsync(_cloneDir, "push", "origin", "master");
    }

    private async Task<string> RunGitCommandAsync(string workDir, params string[] args)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = string.Join(" ", args),
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null) throw new Exception("Failed to start git process.");

        await process.WaitForExitAsync();
        var error = await process.StandardError.ReadToEndAsync();
        var output = await process.StandardOutput.ReadToEndAsync();

        if (process.ExitCode != 0 && !error.Contains("nothing to commit") && !output.Contains("nothing to commit"))
        {
            // Mask the token in error messages
            error = error.Replace(_token, "***");
            throw new Exception($"Git command failed: {error}");
        }

        return output;
    }

    private string GetFullPath(string path)
    {
        var combined = Path.GetFullPath(Path.Combine(_cloneDir, path.TrimStart('/', '\\')));
        if (!combined.StartsWith(_cloneDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Directory traversal is not allowed in the Wiki.");
        }
        return combined;
    }

    private string EnsureMdExtension(string path)
    {
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return path + ".md";
        }
        return path;
    }

    public async Task<string> GetPageAsync(string path)
    {
        try
        {
            await SyncWikiAsync();
            var fullPath = GetFullPath(EnsureMdExtension(path));
            if (!File.Exists(fullPath)) return $"Error: Wiki page '{path}' does not exist.";
            
            return await File.ReadAllTextAsync(fullPath);
        }
        catch (Exception ex)
        {
            return $"Error getting wiki page: {ex.Message}";
        }
    }

    public async Task<string> CreatePageAsync(string title, string content, string? parentPath = null)
    {
        try
        {
            await SyncWikiAsync();

            var fileName = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            fileName = EnsureMdExtension(fileName);

            var dir = _cloneDir;
            if (!string.IsNullOrWhiteSpace(parentPath))
            {
                dir = GetFullPath(parentPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            var fullPath = Path.Combine(dir, fileName);
            if (File.Exists(fullPath)) return $"Error: Wiki page '{fileName}' already exists at this location.";

            await File.WriteAllTextAsync(fullPath, content);
            await CommitAndPushAsync($"Create wiki page: {title}");

            var relativePath = Path.GetRelativePath(_cloneDir, fullPath);
            return $"Successfully created wiki page: {relativePath}";
        }
        catch (Exception ex)
        {
            return $"Error creating wiki page: {ex.Message}";
        }
    }

    public async Task<string> UpdatePageAsync(string path, string content)
    {
        try
        {
            await SyncWikiAsync();

            var fullPath = GetFullPath(EnsureMdExtension(path));
            if (!File.Exists(fullPath)) return $"Error: Wiki page '{path}' does not exist.";

            await File.WriteAllTextAsync(fullPath, content);
            await CommitAndPushAsync($"Update wiki page: {path}");

            return $"Successfully updated wiki page: {path}";
        }
        catch (Exception ex)
        {
            return $"Error updating wiki page: {ex.Message}";
        }
    }

    public async Task<string> SearchPagesAsync(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return "Error: Search query cannot be empty.";

            await SyncWikiAsync();

            var files = Directory.GetFiles(_cloneDir, "*.md", SearchOption.AllDirectories);
            var results = new List<string>();

            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file);
                if (content.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                    Path.GetFileNameWithoutExtension(file).Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(Path.GetRelativePath(_cloneDir, file));
                }
            }

            if (!results.Any()) return $"No pages found matching '{query}'.";

            return $"Found in {results.Count} pages:\n- " + string.Join("\n- ", results);
        }
        catch (Exception ex)
        {
            return $"Error searching wiki pages: {ex.Message}";
        }
    }

    public async Task<string> MovePageAsync(string pathOrId, string newPathOrParentId, string? newTitle = null)
    {
        try
        {
            await SyncWikiAsync();

            var sourcePath = GetFullPath(EnsureMdExtension(pathOrId));
            if (!File.Exists(sourcePath)) return $"Error: Wiki page '{pathOrId}' does not exist.";

            // Determine the target directory
            var targetDir = string.IsNullOrWhiteSpace(newPathOrParentId)
                ? _cloneDir
                : GetFullPath(newPathOrParentId);

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Determine the target filename
            string targetFileName;
            if (!string.IsNullOrWhiteSpace(newTitle))
            {
                var slug = Regex.Replace(newTitle.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                targetFileName = EnsureMdExtension(slug);
            }
            else
            {
                targetFileName = Path.GetFileName(sourcePath);
            }

            var destPath = Path.Combine(targetDir, targetFileName);

            if (File.Exists(destPath) && !string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                return $"Error: A wiki page already exists at the destination: {Path.GetRelativePath(_cloneDir, destPath)}";
            }

            File.Move(sourcePath, destPath, overwrite: false);
            var relDest = Path.GetRelativePath(_cloneDir, destPath);
            await CommitAndPushAsync($"Move wiki page: {pathOrId} -> {relDest}");

            return $"Successfully moved wiki page to: {relDest}";
        }
        catch (Exception ex)
        {
            return $"Error moving wiki page: {ex.Message}";
        }
    }
}
