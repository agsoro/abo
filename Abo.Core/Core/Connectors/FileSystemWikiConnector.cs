using System.Text.RegularExpressions;

namespace Abo.Core.Connectors;

public class FileSystemWikiConnector : IWikiConnector
{
    private readonly ConnectorEnvironment _environment;
    private readonly string _wikiRoot;

    public FileSystemWikiConnector(ConnectorEnvironment environment)
    {
        _environment = environment;
        
        if (!string.IsNullOrWhiteSpace(_environment.WikiDir))
        {
            _wikiRoot = Path.GetFullPath(_environment.WikiDir);
        }
        else
        {
            var subPath = _environment.Wiki?.RootPath ?? "docs";
            _wikiRoot = Path.GetFullPath(Path.Combine(_environment.Dir, subPath.TrimStart('/', '\\')));
        }
        
        if (!Directory.Exists(_wikiRoot))
        {
            Directory.CreateDirectory(_wikiRoot);
        }
    }

    private string GetFullPath(string path)
    {
        // Treat path as relative to the wiki root
        var combined = Path.GetFullPath(Path.Combine(_wikiRoot, path.TrimStart('/', '\\')));
        if (!combined.StartsWith(_wikiRoot, StringComparison.OrdinalIgnoreCase))
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
               currentDir.StartsWith(_wikiRoot, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentDir, _wikiRoot, StringComparison.OrdinalIgnoreCase) &&
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
            var fileName = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            fileName = EnsureMdExtension(fileName);

            var dir = _wikiRoot;
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
            var relativePath = Path.GetRelativePath(_wikiRoot, fullPath);
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
            var fullPath = GetFullPath(EnsureMdExtension(path));
            if (!File.Exists(fullPath)) return $"Error: Wiki page '{path}' does not exist.";

            await File.WriteAllTextAsync(fullPath, content);
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

            var files = Directory.GetFiles(_wikiRoot, "*.md", SearchOption.AllDirectories);
            var results = new List<string>();

            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file);
                if (content.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                    Path.GetFileNameWithoutExtension(file).Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(Path.GetRelativePath(_wikiRoot, file));
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

    public Task<string> MovePageAsync(string pathOrId, string newPathOrParentId, string? newTitle = null)
    {
        try
        {
            var sourcePath = GetFullPath(EnsureMdExtension(pathOrId));
            if (!File.Exists(sourcePath)) return Task.FromResult($"Error: Wiki page '{pathOrId}' does not exist.");

            // Determine the target directory
            var targetDir = string.IsNullOrWhiteSpace(newPathOrParentId)
                ? _wikiRoot
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
                return Task.FromResult($"Error: A wiki page already exists at the destination: {Path.GetRelativePath(_wikiRoot, destPath)}");
            }

            File.Move(sourcePath, destPath, overwrite: false);
            
            CleanupEmptyDirectories(Path.GetDirectoryName(sourcePath));
            
            var relDest = Path.GetRelativePath(_wikiRoot, destPath);
            return Task.FromResult($"Successfully moved wiki page to: {relDest}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error moving wiki page: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<string> PatchPageAsync(string path, string patch)
    {
        // Security: Get full path (prevents directory traversal)
        var fullPath = GetFullPath(EnsureMdExtension(path));

        // 1. Read existing page content or start with empty content
        string originalContent = string.Empty;
        if (File.Exists(fullPath))
        {
            try
            {
                originalContent = await File.ReadAllTextAsync(fullPath);
            }
            catch (Exception ex)
            {
                return $"Error reading wiki page '{path}': {ex.Message}";
            }
        }

        // Split into lines for processing
        var originalLines = originalContent.Split('\n');

        // 2. Parse the unified diff/patch
        var lines = patch.Split('\n');
        int lineIndex = 0;

        // Parse header: --- a/file.txt and +++ b/file.txt
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

        // Parse hunks
        var resultLines = new List<string>();
        int originalLineIndex = 0;

        while (lineIndex < lines.Length)
        {
            var currentLine = lines[lineIndex];

            // Skip any non-hunk header lines
            if (!currentLine.StartsWith("@@ "))
            {
                lineIndex++;
                continue;
            }

            // Parse hunk header: @@ -oldStart,oldCount +newStart,newCount @@
            var hunkMatch = Regex.Match(currentLine, @"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
            if (!hunkMatch.Success)
            {
                return "Error: Invalid patch format - missing hunk header '@@'.";
            }

            int hunkOldStart = int.Parse(hunkMatch.Groups[1].Value);
            int hunkOldCount = hunkMatch.Groups[2].Success ? int.Parse(hunkMatch.Groups[2].Value) : 1;
            lineIndex++;

            // Copy context lines before the hunk
            while (originalLineIndex < hunkOldStart - 1)
            {
                if (originalLineIndex >= originalLines.Length)
                {
                    return $"Error: Patch target line {originalLineIndex + 1} is out of range.";
                }
                resultLines.Add(originalLines[originalLineIndex]);
                originalLineIndex++;
            }

            // Process hunk body
            int hunkLineIndex = 0;

            while (hunkLineIndex < hunkOldCount + 50) // Reasonable limit for hunk body
            {
                if (lineIndex >= lines.Length) break;

                currentLine = lines[lineIndex];

                // Hunk trailer
                if (currentLine == "\\")
                {
                    lineIndex++;
                    hunkLineIndex++;
                    continue;
                }

                // Skip empty lines at end of hunk
                if (string.IsNullOrEmpty(currentLine) && lineIndex == lines.Length - 1)
                {
                    break;
                }

                char lineType = currentLine.Length > 0 ? currentLine[0] : ' ';

                if (lineType == '-')
                {
                    // Deletion - skip original line
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
                    // Addition - add new line
                    string newContent = currentLine.Length > 1 ? currentLine.Substring(1) : "";
                    resultLines.Add(newContent);
                    lineIndex++;
                    hunkLineIndex++;
                }
                else if (lineType == ' ')
                {
                    // Context line - copy original line
                    if (originalLineIndex >= originalLines.Length)
                    {
                        return $"Error: Patch target line {hunkLineIndex + 1} does not match file content.";
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

        // Copy any remaining lines from original page
        while (originalLineIndex < originalLines.Length)
        {
            resultLines.Add(originalLines[originalLineIndex]);
            originalLineIndex++;
        }

        // 3. Write the modified content back
        try
        {
            var newContent = string.Join("\n", resultLines);
            await File.WriteAllTextAsync(fullPath, newContent);
            return $"Successfully applied patch to wiki page: {path}";
        }
        catch (Exception ex)
        {
            return $"Error writing wiki page '{path}': {ex.Message}";
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<WikiPageSummary>> ListPagesAsync(string? parentPath = null)
    {
        try
        {
            var searchDir = string.IsNullOrWhiteSpace(parentPath)
                ? _wikiRoot
                : GetFullPath(parentPath.TrimStart('/', '\\'));

            if (!Directory.Exists(searchDir))
            {
                return Task.FromResult<IEnumerable<WikiPageSummary>>(Array.Empty<WikiPageSummary>());
            }

            var files = Directory.GetFiles(searchDir, "*.md", SearchOption.AllDirectories);
            var pages = new List<WikiPageSummary>();

            foreach (var file in files)
            {
                try
                {
                    var relativePath = Path.GetRelativePath(_wikiRoot, file);
                    var content = File.ReadAllText(file);
                    var title = ExtractTitle(content) ?? Path.GetFileNameWithoutExtension(file);
                    var lastModified = File.GetLastWriteTimeUtc(file);
                    
                    // Determine parent path from directory structure
                    var fileDir = Path.GetDirectoryName(file);
                    string? parent = null;
                    if (!string.IsNullOrWhiteSpace(fileDir) && 
                        !fileDir.Equals(_wikiRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        parent = Path.GetRelativePath(_wikiRoot, fileDir);
                    }

                    pages.Add(new WikiPageSummary(relativePath, title, lastModified, parent));
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            return Task.FromResult<IEnumerable<WikiPageSummary>>(
                pages.OrderBy(p => p.ParentPath).ThenBy(p => p.Title));
        }
        catch
        {
            return Task.FromResult<IEnumerable<WikiPageSummary>>(Array.Empty<WikiPageSummary>());
        }
    }

    private static string? ExtractTitle(string content)
    {
        // Try to extract title from first H1 heading (# Title)
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# "))
            {
                return trimmed.Substring(2).Trim();
            }
        }
        return null;
    }
}
