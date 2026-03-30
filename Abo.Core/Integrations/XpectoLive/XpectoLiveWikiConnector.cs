using System.Text.RegularExpressions;
using Abo.Core.Connectors;
using Abo.Integrations.XpectoLive.Models;

namespace Abo.Integrations.XpectoLive;

public class XpectoLiveWikiConnector : IWikiConnector
{
    private readonly IXpectoLiveWikiClient _client;
    private readonly string _spaceId;

    public XpectoLiveWikiConnector(IXpectoLiveWikiClient client, string spaceId)
    {
        _client = client;
        _spaceId = string.IsNullOrWhiteSpace(spaceId) ? throw new ArgumentException("Space ID is required for XpectoLive Wiki") : spaceId;
    }

    public async Task<string> GetPageAsync(string path)
    {
        try
        {
            var page = await _client.GetPageAsync(_spaceId, path);
            return page.Content ?? "No Content";
        }
        catch (Exception ex) { return $"Error getting wiki page: {ex.Message}"; }
    }

    public async Task<string> CreatePageAsync(string title, string content, string? parentPath = null)
    {
        try
        {
            var newPage = new PageNew { Title = title, ParentId = parentPath };
            var created = await _client.CreatePageAsync(_spaceId, newPage);
            
            if (created.Id != null && !string.IsNullOrEmpty(content))
            {
                await _client.UpdatePageDraftAsync(_spaceId, created.Id, new ContentUpdate { Content = content });
                await _client.PublishPageDraftAsync(_spaceId, created.Id);
            }
            return $"Successfully created wiki page '{title}' with ID: {created.Id}";
        }
        catch (Exception ex) { return $"Error creating wiki page: {ex.Message}"; }
    }

    public async Task<string> UpdatePageAsync(string path, string content)
    {
        try
        {
            await _client.UpdatePageDraftAsync(_spaceId, path, new ContentUpdate { Content = content });
            var pub = await _client.PublishPageDraftAsync(_spaceId, path);
            return $"Successfully updated wiki page with ID: {pub.Id}";
        }
        catch (Exception ex) { return $"Error updating wiki page: {ex.Message}"; }
    }

    public async Task<string> SearchPagesAsync(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query)) return "Error: Search query cannot be empty.";

            var info = await _client.GetSpaceInfoAsync(_spaceId);
            var results = info.Where(p => (p.PageTitle ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                              .Select(p => $"{p.PageID} ({p.PageTitle})");
            
            if (!results.Any()) return $"No pages found in space '{_spaceId}' matching '{query}'.";
            
            return $"Found in {results.Count()} pages:\n- " + string.Join("\n- ", results);
        }
        catch (Exception ex) { return $"Error searching wiki pages: {ex.Message}"; }
    }

    public async Task<string> MovePageAsync(string pathOrId, string newPathOrParentId, string? newTitle = null)
    {
        try
        {
            await _client.MovePageAsync(_spaceId, pathOrId, new MovePageRequest
            {
                TargetSpaceId = _spaceId,
                TargetParentId = string.IsNullOrWhiteSpace(newPathOrParentId) ? null : newPathOrParentId
            });

            if (!string.IsNullOrWhiteSpace(newTitle))
            {
                await _client.UpdatePageDraftAsync(_spaceId, pathOrId, new ContentUpdate { Title = newTitle });
                await _client.PublishPageDraftAsync(_spaceId, pathOrId);
            }

            return $"Successfully moved wiki page '{pathOrId}' to parent '{newPathOrParentId}'" +
                   (string.IsNullOrWhiteSpace(newTitle) ? "." : $" and renamed to '{newTitle}'.");
        }
        catch (Exception ex) { return $"Error moving wiki page: {ex.Message}"; }
    }

    /// <inheritdoc />
    public async Task<string> PatchPageAsync(string path, string patch)
    {
        try
        {
            // 1. Get the current page content
            var page = await _client.GetPageAsync(_spaceId, path);
            var originalContent = page.Content ?? string.Empty;

            // 2. Apply the unified diff/patch
            var newContent = ApplyPatch(originalContent, patch);
            if (newContent.StartsWith("Error:"))
            {
                return newContent;
            }

            // 3. Update and publish the page
            await _client.UpdatePageDraftAsync(_spaceId, path, new ContentUpdate { Content = newContent });
            var pub = await _client.PublishPageDraftAsync(_spaceId, path);

            return $"Successfully applied patch to wiki page: {path}";
        }
        catch (Exception ex) { return $"Error patching wiki page: {ex.Message}"; }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<WikiPageSummary>> ListPagesAsync(string? parentPath = null)
    {
        try
        {
            var spaceInfo = await _client.GetSpaceInfoAsync(_spaceId);
            
            // Filter by parent if specified (SpacePageInfo doesn't have ParentID, so we list all pages)
            // The parentPath parameter is kept for interface compatibility but filtering by parent
            // is not supported by the current XpectoLive API
            var pages = spaceInfo
                .Select(p => new WikiPageSummary(
                    p.PageID ?? "",
                    p.PageTitle ?? "Untitled",
                    p.ActionTimestamp,
                    null))   // ParentID not available in SpacePageInfo
                .ToList();

            return pages.OrderBy(p => p.Title);
        }
        catch
        {
            return Array.Empty<WikiPageSummary>();
        }
    }

    /// <inheritdoc />
    public async Task<string> ListWikiAsync(string path)
    {
        try
        {
            // For XpectoLive, we list pages from the space info API
            // The path parameter is ignored since the API doesn't support directory-like navigation
            var spaceInfo = await _client.GetSpaceInfoAsync(_spaceId);

            if (!spaceInfo.Any())
            {
                return $"Wiki space '{_spaceId}' is empty.";
            }

            // Sort pages by title for a tree-like view
            var sortedPages = spaceInfo
                .OrderBy(p => p.PageTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new System.Text.StringBuilder();
            result.AppendLine($"Wiki space: {_spaceId}");
            result.AppendLine(new string('-', 40));

            foreach (var page in sortedPages)
            {
                var pageId = page.PageID ?? "unknown";
                var title = page.PageTitle ?? "Untitled";
                result.AppendLine($"  ├── {title} (ID: {pageId})");
            }

            result.AppendLine(new string('-', 40));
            result.AppendLine($"Total: {sortedPages.Count} page(s)");

            return result.ToString();
        }
        catch (UnauthorizedAccessException)
        {
            return "Error: Access denied to wiki space.";
        }
        catch (Exception ex) { return $"Error listing wiki: {ex.Message}"; }
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
