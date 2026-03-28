using Xunit;
using Abo.Core.Connectors;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the write_file tool — covers LocalWorkspaceConnector.WriteFileAsync
/// markdown file restrictions in the abo environment.
/// </summary>
[Trait("Category", "Unit")]
public class WriteFileToolTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Shared infrastructure
    // -------------------------------------------------------------------------

    private readonly string _tempDir;

    public WriteFileToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WriteFileToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helper — creates a connector with the specified environment name
    // -------------------------------------------------------------------------

    private LocalWorkspaceConnector CreateConnector(string environmentName)
    {
        return new LocalWorkspaceConnector(new ConnectorEnvironment
        {
            Name = environmentName,
            Dir = _tempDir
        });
    }

    // =========================================================================
    // Markdown file restrictions in abo environment
    // =========================================================================

    [Fact]
    public async Task WriteFileAsync_README_md_AllowedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("README.md", "# Test README");

        // Assert - should succeed
        Assert.StartsWith("Successfully wrote", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "README.md")));
    }

    [Fact]
    public async Task WriteFileAsync_LowercaseReadme_md_AllowedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("readme.md", "# Test readme");

        // Assert - should succeed (case-insensitive match)
        Assert.StartsWith("Successfully wrote", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "readme.md")));
    }

    [Fact]
    public async Task WriteFileAsync_MixedCaseReadme_md_AllowedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("Readme.MD", "# Test Readme");

        // Assert - should succeed (case-insensitive match)
        Assert.StartsWith("Successfully wrote", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "Readme.MD")));
    }

    [Fact]
    public async Task WriteFileAsync_OtherMdFile_BlockedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("notes.md", "# My Notes");

        // Assert - should be blocked with guidance message
        Assert.StartsWith("Error:", result);
        Assert.Contains("Writing .md files is restricted", result);
        Assert.Contains("add_issue_comment", result);
        Assert.Contains("wiki", result);
        Assert.False(File.Exists(Path.Combine(_tempDir, "notes.md")));
    }

    [Fact]
    public async Task WriteFileAsync_MdFileWithSubdirectory_BlockedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("docs/changelog.md", "# Changelog");

        // Assert - should be blocked (filename is still changelog.md)
        Assert.StartsWith("Error:", result);
        Assert.Contains("Writing .md files is restricted", result);
        Assert.False(File.Exists(Path.Combine(_tempDir, "docs", "changelog.md")));
    }

    [Fact]
    public async Task WriteFileAsync_CapitalMdExtension_BlockedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("ARCHITECTURE.MD", "# Architecture");

        // Assert - should be blocked (case-insensitive .md check)
        Assert.StartsWith("Error:", result);
        Assert.Contains("Writing .md files is restricted", result);
        Assert.False(File.Exists(Path.Combine(_tempDir, "ARCHITECTURE.MD")));
    }

    [Fact]
    public async Task WriteFileAsync_AboEnvironment_CaseInsensitiveMatch()
    {
        // Arrange - test various case combinations of "abo"
        var testCases = new[] { "ABO", "Abo", "aBo", "abo" };

        foreach (var envName in testCases)
        {
            // Use a unique subdirectory per test case
            var testDir = Path.Combine(_tempDir, envName);
            Directory.CreateDirectory(testDir);
            var connector = new LocalWorkspaceConnector(new ConnectorEnvironment
            {
                Name = envName,
                Dir = testDir
            });

            // Act
            var result = await connector.WriteFileAsync("other.md", "# Other");

            // Assert - should be blocked for all case variations
            Assert.StartsWith("Error:", result);
            Assert.Contains("Writing .md files is restricted", result);

            // Cleanup
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task WriteFileAsync_MdFile_AllowedInNonAboEnv()
    {
        // Arrange - test with non-abo environments
        var environments = new[] { "production", "staging", "local", "dev", "" };

        foreach (var envName in environments)
        {
            var envSubdir = envName ?? "empty";
            var testDir = Path.Combine(_tempDir, envSubdir);
            Directory.CreateDirectory(testDir);
            var connector = new LocalWorkspaceConnector(new ConnectorEnvironment
            {
                Name = envName!,
                Dir = testDir
            });

            // Act
            var result = await connector.WriteFileAsync("architecture.md", "# Architecture");

            // Assert - should succeed for non-abo environments
            Assert.StartsWith("Successfully wrote", result);
            Assert.True(File.Exists(Path.Combine(testDir, "architecture.md")));

            // Cleanup
            Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task WriteFileAsync_TxtFile_AllowedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("notes.txt", "Some notes");

        // Assert - non-.md files should still work in abo
        Assert.StartsWith("Successfully wrote", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "notes.txt")));
    }

    [Fact]
    public async Task WriteFileAsync_JsonFile_AllowedInAbo()
    {
        // Arrange
        var connector = CreateConnector("abo");

        // Act
        var result = await connector.WriteFileAsync("config.json", "{}");

        // Assert - non-.md files should still work in abo
        Assert.StartsWith("Successfully wrote", result);
        Assert.True(File.Exists(Path.Combine(_tempDir, "config.json")));
    }
}
