using System.Text;
using Abo.Core.Connectors;
using Abo.Tools.Connector;
using Moq;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the read_file tool — covers both LocalWorkspaceConnector.ReadFileAsync
/// and ReadFileTool.ExecuteAsync.
/// </summary>
public class ReadFileToolTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Shared infrastructure
    // -------------------------------------------------------------------------

    private readonly string _tempDir;
    private readonly LocalWorkspaceConnector _connector;

    public ReadFileToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ReadFileToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _connector = new LocalWorkspaceConnector(new ConnectorEnvironment { Dir = _tempDir });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helper — creates a file of exactly `sizeBytes` bytes in the temp dir
    // -------------------------------------------------------------------------

    private async Task<string> CreateFileOfSizeAsync(string fileName, int sizeBytes)
    {
        var filePath = Path.Combine(_tempDir, fileName);
        // Fill with ASCII 'A' characters so it is valid UTF-8 text
        var content = new string('A', sizeBytes);
        await File.WriteAllTextAsync(filePath, content, Encoding.ASCII);
        return fileName;
    }

    // =========================================================================
    // LocalWorkspaceConnector.ReadFileAsync tests
    // =========================================================================

    [Fact]
    public async Task ReadFileAsync_StandardLimit_BlocksFileLargerThan50KB()
    {
        // Arrange — create a file that is 1 byte over the standard limit
        var fileName = await CreateFileOfSizeAsync("big_standard.txt", LocalWorkspaceConnector.StandardReadLimitBytes + 1);

        // Act
        var result = await _connector.ReadFileAsync(fileName, important: false);

        // Assert — must be an error referencing the 50KB cap
        Assert.StartsWith("Error:", result);
        Assert.Contains("50KB", result);
        // Must NOT mention the 250KB limit as the limiting factor
        Assert.DoesNotContain("250KB", result);
    }

    [Fact]
    public async Task ReadFileAsync_ImportantLimit_BlocksFileLargerThan250KB()
    {
        // Arrange — create a file that is 1 byte over the important limit
        var fileName = await CreateFileOfSizeAsync("big_important.txt", LocalWorkspaceConnector.ImportantReadLimitBytes + 1);

        // Act
        var result = await _connector.ReadFileAsync(fileName, important: true);

        // Assert — must be an error referencing the 250KB cap
        Assert.StartsWith("Error:", result);
        Assert.Contains("250KB", result);
    }

    [Fact]
    public async Task ReadFileAsync_ImportantFlag_AllowsFileBetween50KBAnd250KB()
    {
        // Arrange — create a file that exceeds the standard limit but fits within the important limit
        int sizeBytes = LocalWorkspaceConnector.StandardReadLimitBytes + 1024; // ~51 KB
        var fileName = await CreateFileOfSizeAsync("medium_important.txt", sizeBytes);

        // Act — without important flag should fail, with important flag should succeed
        var normalResult = await _connector.ReadFileAsync(fileName, important: false);
        var importantResult = await _connector.ReadFileAsync(fileName, important: true);

        // Assert
        Assert.StartsWith("Error:", normalResult);
        Assert.DoesNotMatch("^Error:", importantResult); // should be the file content
        Assert.Equal(sizeBytes, importantResult.Length);
    }

    [Fact]
    public async Task ReadFileAsync_DefaultParameter_BehavesLikeImportantFalse()
    {
        // Arrange — create a file slightly above the standard limit
        var fileName = await CreateFileOfSizeAsync("default_param.txt", LocalWorkspaceConnector.StandardReadLimitBytes + 1);

        // Act — call without the important parameter (should default to false)
        var defaultResult = await _connector.ReadFileAsync(fileName);
        var explicitFalseResult = await _connector.ReadFileAsync(fileName, important: false);

        // Assert — both must produce the same error
        Assert.Equal(defaultResult, explicitFalseResult);
        Assert.StartsWith("Error:", defaultResult);
    }

    [Fact]
    public async Task ReadFileAsync_ErrorMessages_AreDistinct()
    {
        // Arrange — one file exceeds both limits, another exceeds only the standard limit
        var bigFile = await CreateFileOfSizeAsync("really_big.txt", LocalWorkspaceConnector.ImportantReadLimitBytes + 1);
        var mediumFile = await CreateFileOfSizeAsync("medium.txt", LocalWorkspaceConnector.StandardReadLimitBytes + 1);

        // Act
        var normalError = await _connector.ReadFileAsync(mediumFile, important: false);
        var importantError = await _connector.ReadFileAsync(bigFile, important: true);

        // Assert — messages must be distinct and carry different contextual information
        Assert.NotEqual(normalError.Replace(mediumFile, "<file>"), importantError.Replace(bigFile, "<file>"));

        // Normal error should hint at the important parameter
        Assert.Contains("important=true", normalError);

        // Important error must NOT suggest retrying with important=true (already at max)
        Assert.DoesNotContain("important=true", importantError);
    }

    // =========================================================================
    // ReadFileTool.ExecuteAsync tests
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_WithImportantTrue_PassesImportantFlagToConnector()
    {
        // Arrange
        var mockConnector = new Mock<IWorkspaceConnector>();
        mockConnector
            .Setup(c => c.ReadFileAsync("src/main.cs", true))
            .ReturnsAsync("file content");

        var tool = new ReadFileTool(mockConnector.Object);
        var json = """{"relativePath":"src/main.cs","important":true}""";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.Equal("file content", result);
        mockConnector.Verify(c => c.ReadFileAsync("src/main.cs", true), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithImportantFalse_PassesFalseToConnector()
    {
        // Arrange
        var mockConnector = new Mock<IWorkspaceConnector>();
        mockConnector
            .Setup(c => c.ReadFileAsync("src/main.cs", false))
            .ReturnsAsync("file content");

        var tool = new ReadFileTool(mockConnector.Object);
        var json = """{"relativePath":"src/main.cs","important":false}""";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.Equal("file content", result);
        mockConnector.Verify(c => c.ReadFileAsync("src/main.cs", false), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithOmittedImportant_DefaultsToFalse()
    {
        // Arrange
        var mockConnector = new Mock<IWorkspaceConnector>();
        mockConnector
            .Setup(c => c.ReadFileAsync("src/main.cs", false))
            .ReturnsAsync("file content");

        var tool = new ReadFileTool(mockConnector.Object);
        var json = """{"relativePath":"src/main.cs"}""";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.Equal("file content", result);
        mockConnector.Verify(c => c.ReadFileAsync("src/main.cs", false), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRelativePath_ReturnsError()
    {
        // Arrange
        var mockConnector = new Mock<IWorkspaceConnector>();
        var tool = new ReadFileTool(mockConnector.Object);
        var json = """{"important":true}""";

        // Act
        var result = await tool.ExecuteAsync(json);

        // Assert
        Assert.StartsWith("Error:", result);
        Assert.Contains("relativePath", result);
        mockConnector.Verify(c => c.ReadFileAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }
}
