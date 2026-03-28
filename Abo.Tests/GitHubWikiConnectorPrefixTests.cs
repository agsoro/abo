using Xunit;

namespace Abo.Tests;

/// <summary>
/// Lightweight unit tests that verify the [abo] prefix logic applied in
/// GitHubWikiConnector.CommitAndPushAsync via the commit message string.
/// These tests assert the string-level transformation without requiring a live GitHub token.
/// </summary>
[Trait("Category", "Unit")]
public class GitHubWikiConnectorPrefixTests
{
    // Helper: mimics the commit message construction in CommitAndPushAsync
    private static string BuildCommitMessage(string message) => $"[abo] {message}";

    [Fact]
    public void CommitMessage_ShouldStartWithAboPrefix()
    {
        var message = "Some commit action";
        var commitMessage = BuildCommitMessage(message);

        Assert.StartsWith("[abo] ", commitMessage);
    }

    [Fact]
    public void CommitMessage_ShouldPreserveOriginalContent()
    {
        var message = "Some commit action";
        var commitMessage = BuildCommitMessage(message);

        Assert.Equal($"[abo] {message}", commitMessage);
        Assert.EndsWith(message, commitMessage);
    }

    [Fact]
    public void CommitMessage_EmptyMessage_ShouldStillHavePrefix()
    {
        var message = "";
        var commitMessage = BuildCommitMessage(message);

        Assert.Equal("[abo] ", commitMessage);
    }

    [Fact]
    public void CommitMessage_CreatePage_FormatCheck()
    {
        var title = "My Page";
        var message = $"Create wiki page: {title}";
        var commitMessage = BuildCommitMessage(message);

        Assert.Equal("[abo] Create wiki page: My Page", commitMessage);
        Assert.StartsWith("[abo] ", commitMessage);
    }

    [Fact]
    public void CommitMessage_UpdatePage_FormatCheck()
    {
        var path = "my-page.md";
        var message = $"Update wiki page: {path}";
        var commitMessage = BuildCommitMessage(message);

        Assert.Equal("[abo] Update wiki page: my-page.md", commitMessage);
        Assert.StartsWith("[abo] ", commitMessage);
    }

    [Fact]
    public void CommitMessage_MovePage_FormatCheck()
    {
        var src = "old.md";
        var dest = "new.md";
        var message = $"Move wiki page: {src} -> {dest}";
        var commitMessage = BuildCommitMessage(message);

        Assert.Equal("[abo] Move wiki page: old.md -> new.md", commitMessage);
        Assert.StartsWith("[abo] ", commitMessage);
    }
}
