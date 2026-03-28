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
        _cloneDir = Path.GetFullPath(environment.WikiDir ?? throw new ArgumentException("WikiDir cannot be null", nameof(environment)));
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
        await RunGitCommandAsync(_cloneDir, "commit", "-m", $"\"[abo] {message}\"");
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

    /// <summary>
    /// Resolves a page path/filename to a full filesystem path, rooted in the wiki clone directory.
    /// Supports subdirectories (e.g. "Folder/page.md").
    /// </summary>
    private string GetFullPath(string path)
    {
        var combined = Path.GetFullPath(Path.Combine(_cloneDir, path.TrimStart('/', '\\')));

        // Safety check: ensure the resolved path is still within the clone directory
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

    private void CleanupEmptyDirectories(string? directoryPath)
    {
        var currentDir = directoryPath;
        while (!string.IsNullOrWhiteSpace(currentDir) &&
               currentDir.StartsWith(_cloneDir, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentDir, _cloneDir, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(currentDir))
        {
            if (!Directory.EnumerateFileSystemEntries(currentDir).Any())
            {
                try
                {
                    Directory.Delete(currentDir);
                }
                catch
                {
                    break;
                }
                currentDir = Path.GetDirectoryName(currentDir);
            }
            else
            {
                break;
            }
        }
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
            var relativePath = Path.GetRelativePath(_cloneDir, fullPath);
            if (File.Exists(fullPath)) return $"Error: Wiki page '{relativePath}' already exists at this location.";

            await File.WriteAllTextAsync(fullPath, content);
            await CommitAndPushAsync($"Create wiki page: {relativePath}");

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

            // Split query into individual terms, removing empty entries (e.g., double spaces)
            var searchTerms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            await SyncWikiAsync();

            var files = Directory.GetFiles(_cloneDir, "*.md", SearchOption.AllDirectories);
            var results = new List<string>();

            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var content = await File.ReadAllTextAsync(file);

                // Check if ANY search term matches either the content or the filename
                bool isMatch = searchTerms.Any(term =>
                    content.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains(term, StringComparison.OrdinalIgnoreCase));

                if (isMatch)
                {
                    results.Add(Path.GetRelativePath(_cloneDir, file));
                }
            }

            if (!results.Any()) return $"No pages found matching any terms in '{query}'.";

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
            
            CleanupEmptyDirectories(Path.GetDirectoryName(sourcePath));
            
            var relDest = Path.GetRelativePath(_cloneDir, destPath);
            await CommitAndPushAsync($"Move wiki page: {pathOrId} -> {relDest}");

            return $"Successfully moved wiki page to: {relDest}";
        }
        catch (Exception ex)
        {
            return $"Error moving wiki page: {ex.Message}";
        }
    }

    /// <inheritdoc />
    public async Task<string> PatchPageAsync(string path, string patch)
    {
        try
        {
            await SyncWikiAsync();

            var fullPath = GetFullPath(EnsureMdExtension(path));

            // 1. Read existing page content or start with empty content
            string originalContent = string.Empty;
            if (File.Exists(fullPath))
            {
                originalContent = await File.ReadAllTextAsync(fullPath);
            }

            // 2. Parse and apply the unified diff/patch
            var result = ApplyPatch(originalContent, patch);
            if (result.StartsWith("Error:"))
            {
                return result;
            }

            // 3. Write the modified content back
            await File.WriteAllTextAsync(fullPath, result);
            await CommitAndPushAsync($"Patch wiki page: {path}");

            return $"Successfully applied patch to wiki page: {path}";
        }
        catch (Exception ex)
        {
            return $"Error patching wiki page: {ex.Message}";
        }
    }

    private string ApplyPatch(string originalContent, string patch)
    {
        var originalLines = originalContent.Split('\n');
        var lines = patch.Split('\n');
        int lineIndex = 0;

        // Parse header
        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("--- "))
        {
            return "Error: Invalid patch format - missing '---' header.";
        }
        lineIndex++;

        if (lineIndex >= lines.Length || !lines[lineIndex].StartsWith("+++ "))
        {
            return "Error: Invalid patch format - missing '+++' header.";
        }
        lineIndex++;

        var resultLines = new List<string>();
        int originalLineIndex = 0;

        while (lineIndex < lines.Length)
        {
            var currentLine = lines[lineIndex];

            if (!currentLine.StartsWith("@@ "))
            {
                lineIndex++;
                continue;
            }

            var hunkMatch = Regex.Match(currentLine, @"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
            if (!hunkMatch.Success)
            {
                return "Error: Invalid patch format - missing hunk header '@@'.";
            }

            int hunkOldStart = int.Parse(hunkMatch.Groups[1].Value);
            int hunkOldCount = hunkMatch.Groups[2].Success ? int.Parse(hunkMatch.Groups[2].Value) : 1;
            lineIndex++;

            while (originalLineIndex < hunkOldStart - 1)
            {
                if (originalLineIndex >= originalLines.Length)
                {
                    return $"Error: Patch target line {originalLineIndex + 1} is out of range.";
                }
                resultLines.Add(originalLines[originalLineIndex]);
                originalLineIndex++;
            }

            int hunkLineIndex = 0;

            while (hunkLineIndex < hunkOldCount + 50)
            {
                if (lineIndex >= lines.Length) break;

                currentLine = lines[lineIndex];

                if (currentLine == "\\")
                {
                    lineIndex++;
                    hunkLineIndex++;
                    continue;
                }

                if (string.IsNullOrEmpty(currentLine) && lineIndex == lines.Length - 1)
                {
                    break;
                }

                char lineType = currentLine.Length > 0 ? currentLine[0] : ' ';

                if (lineType == '-')
                {
                    string expectedContent = currentLine.Length > 1 ? currentLine.Substring(1) : "";

                    if (originalLineIndex >= originalLines.Length)
                    {
                        return $"Error: Patch hunk line {hunkLineIndex + 1} does not match file content.";
                    }

                    var actualContent = originalLines[originalLineIndex];
                    if (!actualContent.TrimEnd('\r').EndsWith(expectedContent.TrimEnd('\r')))
                    {
                        return $"Error: Patch hunk line {hunkLineIndex + 1} does not match file content.";
                    }

                    originalLineIndex++;
                    hunkLineIndex++;
                }
                else if (lineType == '+')
                {
                    string newContent = currentLine.Length > 1 ? currentLine.Substring(1) : "";
                    resultLines.Add(newContent);
                    lineIndex++;
                    hunkLineIndex++;
                }
                else if (lineType == ' ')
                {
                    if (originalLineIndex >= originalLines.Length)
                    {
                        return $"Error: Patch target line {originalLineIndex + 1} is out of range.";
                    }

                    resultLines.Add(originalLines[originalLineIndex]);
                    originalLineIndex++;
                    lineIndex++;
                    hunkLineIndex++;
                }
                else
                {
                    lineIndex++;
                    hunkLineIndex++;
                }
            }
        }

        while (originalLineIndex < originalLines.Length)
        {
            resultLines.Add(originalLines[originalLineIndex]);
            originalLineIndex++;
        }

        return string.Join("\n", resultLines);
    }
}
