using System.Text.RegularExpressions;

namespace Abo.Core.Connectors;

public class FileSystemWikiConnector : IWikiConnector
{
    private readonly ConnectorEnvironment _environment;
    private readonly string _wikiRoot;

    public FileSystemWikiConnector(ConnectorEnvironment environment)
    {
        _environment = environment;
        var subPath = _environment.Wiki?.RootPath ?? "docs";
        _wikiRoot = Path.GetFullPath(Path.Combine(_environment.Dir, subPath.TrimStart('/', '\\')));
        
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
            var relDest = Path.GetRelativePath(_wikiRoot, destPath);
            return Task.FromResult($"Successfully moved wiki page to: {relDest}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error moving wiki page: {ex.Message}");
        }
    }
}
