using Xunit;
using Abo.Core.Connectors;
using Abo.Tools.Connector;
using Moq;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the patch_wiki_page tool — covers FileSystemWikiConnector.PatchPageAsync
/// and PatchWikiPageTool.ExecuteAsync.
/// </summary>
[Trait("Category", "Unit")]
public class PatchWikiPageToolTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Shared infrastructure
    // -------------------------------------------------------------------------

    private readonly string _tempDir;
    private readonly FileSystemWikiConnector _connector;

    public PatchWikiPageToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PatchWikiPageToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _connector = new FileSystemWikiConnector(new ConnectorEnvironment { WikiDir = _tempDir });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helper methods
    // -------------------------------------------------------------------------

    private async Task WritePageAsync(string path, string content)
    {
        var fullPath = Path.Combine(_tempDir, path);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, content);
    }

    private async Task<string> ReadPageAsync(string path)
    {
        var fullPath = Path.Combine(_tempDir, path);
        return await File.ReadAllTextAsync(fullPath);
    }

    // =========================================================================
    // FileSystemWikiConnector.PatchPageAsync tests
    // =========================================================================

    [Fact]
    public async Task PatchPageAsync_CreatesNewPage_WhenPageDoesNotExist()
    {
        // Arrange
        var path = "new_page";
        var patch = """
            --- a/new_page.md
            +++ b/new_page.md
            @@ -0,0 +1,3 @@
            +# New Page
            +
            +This is the content.
            """;

        // Act
        var result = await _connector.PatchPageAsync(path, patch);

        // Assert
        Assert.StartsWith("Successfully applied patch", result);
        var content = await ReadPageAsync("new_page.md");
        Assert.Contains("# New Page", content);
        Assert.Contains("This is the content.", content);
    }

    [Fact]
    public async Task PatchPageAsync_AddsNewLines_ToExistingPage()
    {
        // Arrange
        var path = "existing";
        var originalContent = """
            # Documentation

            This is the main section.
            """;
        await WritePageAsync("existing.md", originalContent);

        var patch = """
            --- a/existing.md
            +++ b/existing.md
            @@ -1,3 +1,5 @@
             # Documentation

             This is the main section.
            +
            +## New Section
            """;

        // Act
        var result = await _connector.PatchPageAsync(path, patch);

        // Assert
        Assert.StartsWith("Successfully applied patch", result);
        var content = await ReadPageAsync("existing.md");
        Assert.Contains("## New Section", content);
    }

    [Fact]
    public async Task PatchPageAsync_ReturnsError_WhenMissingHeader()
    {
        // Arrange
        var path = "test";
        var patch = """
            @@ -1,1 +1,1 @@
             old
            +new
            """;

        // Act
        var result = await _connector.PatchPageAsync(path, patch);

        // Assert
        Assert.StartsWith("Error:", result);
        Assert.Contains("missing '---' header", result);
    }

    [Fact]
    public async Task PatchPageAsync_ReturnsError_WhenContextDoesNotMatch()
    {
        // Arrange
        var path = "mismatch";
        var originalContent = "original line\n";
        await WritePageAsync("mismatch.md", originalContent);

        var patch = """
            --- a/mismatch.md
            +++ b/mismatch.md
            @@ -1,1 +1,1 @@
            -WRONG_LINE
            +new line
            """;

        // Act
        var result = await _connector.PatchPageAsync(path, patch);

        // Assert
        Assert.StartsWith("Error:", result);
        Assert.Contains("does not match file content", result);
    }

    [Fact]
    public async Task PatchPageAsync_HandlesDirectoryTraversal()
    {
        // Arrange
        var path = "../outside";
        var patch = """
            --- a/../outside.md
            +++ b/../outside.md
            @@ -0,0 +1 @@
            +This should not be written
            """;

        // Act
        try
        {
            var result = await _connector.PatchPageAsync(path, patch);
            // The connector throws an exception for directory traversal
            Assert.StartsWith("Error:", result);
        }
        catch (UnauthorizedAccessException)
        {
            // This is also acceptable - directory traversal is blocked
            Assert.True(true);
        }
    }

    // =========================================================================
    // PatchWikiPageTool.ExecuteAsync tests
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_MissingPath_ReturnsError()
    {
        // Arrange
        var tool = new PatchWikiPageTool(_connector);
        var json = @"{""patch"":""some patch""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingPatch_ReturnsError()
    {
        // Arrange
        var tool = new PatchWikiPageTool(_connector);
        var json = @"{""pathOrId"":""test.md""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public void PatchWikiPageTool_HasCorrectName()
    {
        // Arrange
        var tool = new PatchWikiPageTool(_connector);

        // Act & Assert
        Assert.Equal("patch_wiki_page", tool.Name);
    }

    [Fact]
    public void PatchWikiPageTool_HasDescription()
    {
        // Arrange
        var tool = new PatchWikiPageTool(_connector);

        // Act & Assert
        Assert.NotNull(tool.Description);
        Assert.NotEmpty(tool.Description);
    }

    [Fact]
    public void PatchWikiPageTool_HasParametersSchema()
    {
        // Arrange
        var tool = new PatchWikiPageTool(_connector);

        // Act & Assert
        Assert.NotNull(tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToConnector()
    {
        // Arrange
        var mockConnector = new Mock<IWikiConnector>();
        mockConnector
            .Setup(c => c.PatchPageAsync("test.md", It.IsAny<string>()))
            .ReturnsAsync("Successfully applied patch to wiki page: test.md.");

        var tool = new PatchWikiPageTool(mockConnector.Object);
        var json = @"{""pathOrId"":""test.md"",""patch"":""--- a/test.md\n+++ b/test.md\n@@ -0,0 +1 @@\n+content""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Successfully applied patch", result);
        mockConnector.Verify(c => c.PatchPageAsync("test.md", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenConnectorFails()
    {
        // Arrange
        var mockConnector = new Mock<IWikiConnector>();
        mockConnector
            .Setup(c => c.PatchPageAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Error: Invalid patch format - missing '---' header.");

        var tool = new PatchWikiPageTool(mockConnector.Object);
        var json = @"{""pathOrId"":""test.md"",""patch"":""invalid patch""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Error:", result);
    }
}
