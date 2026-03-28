using Xunit;
using Abo.Core.Connectors;
using Abo.Tools.Connector;
using Moq;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the patch_file tool — covers both LocalWorkspaceConnector.PatchFileAsync
/// and PatchFileTool.ExecuteAsync.
/// </summary>
[Trait("Category", "Unit")]
public class PatchFileToolTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Shared infrastructure
    // -------------------------------------------------------------------------

    private readonly string _tempDir;
    private readonly LocalWorkspaceConnector _connector;

    public PatchFileToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PatchFileToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _connector = new LocalWorkspaceConnector(new ConnectorEnvironment { Dir = _tempDir });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helper — writes content to a file in the temp dir
    // -------------------------------------------------------------------------

    private async Task WriteFileAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(fullPath, content);
    }

    private async Task<string> ReadFileAsync(string relativePath)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        return await File.ReadAllTextAsync(fullPath);
    }

    // =========================================================================
    // LocalWorkspaceConnector.PatchFileAsync tests
    // =========================================================================

    [Fact]
    public async Task PatchFileAsync_CreatesNewFile_WhenFileDoesNotExist()
    {
        // Arrange
        var relativePath = "new_file.cs";
        var patch = """
            --- a/new_file.cs
            +++ b/new_file.cs
            @@ -0,0 +1,3 @@
            +namespace Test;
            +
            +public class Foo { }
            """;

        // Act
        var result = await _connector.PatchFileAsync(relativePath, patch);

        // Assert
        Assert.StartsWith("Successfully applied patch", result);
        var content = await ReadFileAsync(relativePath);
        Assert.Contains("namespace Test;", content);
        Assert.Contains("public class Foo { }", content);
    }

    [Fact]
    public async Task PatchFileAsync_AddsNewLines_ToExistingFile()
    {
        // Arrange
        var relativePath = "existing.cs";
        var originalContent = """
            namespace Test;

            public class Foo
            {
            }
            """;
        await WriteFileAsync(relativePath, originalContent);

        var patch = """
            --- a/existing.cs
            +++ b/existing.cs
            @@ -2,4 +2,6 @@
             
             public class Foo
             {
            +    public void Bar() { }
             }
            """;

        // Act
        var result = await _connector.PatchFileAsync(relativePath, patch);

        // Assert
        Assert.StartsWith("Successfully applied patch", result);
        var content = await ReadFileAsync(relativePath);
        Assert.Contains("public void Bar() { }", content);
    }

    [Fact]
    public async Task PatchFileAsync_ReturnsError_WhenMissingHeader()
    {
        // Arrange
        var relativePath = "test.cs";
        var patch = """
            @@ -1,1 +1,1 @@
             old
            +new
            """;

        // Act
        var result = await _connector.PatchFileAsync(relativePath, patch);

        // Assert
        Assert.StartsWith("Error:", result);
        Assert.Contains("missing '---' header", result);
    }

    [Fact]
    public async Task PatchFileAsync_ReturnsError_WhenContextDoesNotMatch()
    {
        // Arrange
        var relativePath = "mismatch.cs";
        var originalContent = "original line\n";
        await WriteFileAsync(relativePath, originalContent);

        var patch = """
            --- a/mismatch.cs
            +++ b/mismatch.cs
            @@ -1,1 +1,1 @@
            -WRONG_LINE
            +new line
            """;

        // Act
        var result = await _connector.PatchFileAsync(relativePath, patch);

        // Assert
        Assert.StartsWith("Error:", result);
        Assert.Contains("does not match file content", result);
    }

    [Fact]
    public async Task PatchFileAsync_CreatesParentDirectories_WhenNeeded()
    {
        // Arrange
        var relativePath = "nested/dir/new_file.cs";
        var patch = """
            --- a/nested/dir/new_file.cs
            +++ b/nested/dir/new_file.cs
            @@ -0,0 +1,2 @@
            +namespace Nested;
            +public class Bar { }
            """;

        // Act
        var result = await _connector.PatchFileAsync(relativePath, patch);

        // Assert
        Assert.StartsWith("Successfully applied patch", result);
        var content = await ReadFileAsync(relativePath);
        Assert.Contains("namespace Nested;", content);
    }

    // =========================================================================
    // PatchFileTool.ExecuteAsync tests
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_MissingRelativePath_ReturnsError()
    {
        // Arrange
        var tool = new PatchFileTool(_connector);
        var json = @"{""patch"":""some patch""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Error:", result);
        Assert.Contains("relativePath", result);
    }

    [Fact]
    public async Task ExecuteAsync_MissingPatch_ReturnsError()
    {
        // Arrange
        var tool = new PatchFileTool(_connector);
        var json = @"{""relativePath"":""test.cs""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Error:", result);
        Assert.Contains("patch", result);
    }

    [Fact]
    public void PatchFileTool_HasCorrectName()
    {
        // Arrange
        var tool = new PatchFileTool(_connector);

        // Act & Assert
        Assert.Equal("patch_file", tool.Name);
    }

    [Fact]
    public void PatchFileTool_HasDescription()
    {
        // Arrange
        var tool = new PatchFileTool(_connector);

        // Act & Assert
        Assert.NotNull(tool.Description);
        Assert.NotEmpty(tool.Description);
    }

    [Fact]
    public void PatchFileTool_HasParametersSchema()
    {
        // Arrange
        var tool = new PatchFileTool(_connector);

        // Act & Assert
        Assert.NotNull(tool.ParametersSchema);
    }

    [Fact]
    public async Task ExecuteAsync_DelegatesToConnector()
    {
        // Arrange
        var mockConnector = new Mock<IWorkspaceConnector>();
        mockConnector
            .Setup(c => c.PatchFileAsync("test.cs", It.IsAny<string>()))
            .ReturnsAsync("Successfully applied patch to 'test.cs'.");

        var tool = new PatchFileTool(mockConnector.Object);
        // Use a simple patch without newlines to avoid JSON escaping issues in test
        var json = @"{""relativePath"":""test.cs"",""patch"":""--- a/test.cs\n+++ b/test.cs\n@@ -0,0 +1 @@\n+content""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Successfully applied patch", result);
        mockConnector.Verify(c => c.PatchFileAsync("test.cs", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenConnectorFails()
    {
        // Arrange
        var mockConnector = new Mock<IWorkspaceConnector>();
        mockConnector
            .Setup(c => c.PatchFileAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Error: Invalid patch format - missing '---' header.");

        var tool = new PatchFileTool(mockConnector.Object);
        var json = @"{""relativePath"":""test.cs"",""patch"":""invalid patch""}";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Error:", result);
    }
}
