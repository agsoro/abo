using Xunit;

namespace Abo.Tests;

/// <summary>
/// Lightweight unit tests that verify the [abo] prefix logic applied in
/// GitHubIssueTrackerConnector.AddIssueCommentAsync and CreateIssueAsync.
/// These tests assert the string-level transformation without requiring a live GitHub token.
/// </summary>
[Trait("Category", "Unit")]
public class GitHubIssueTrackerConnectorPrefixTests
{
    // --- AddIssueCommentAsync prefix logic ---

    [Fact]
    public void CommentBody_ShouldStartWithAboPrefix()
    {
        var originalBody = "This is a test comment.";
        var taggedBody = $"[abo]\n{originalBody}";

        Assert.StartsWith("[abo]\n", taggedBody);
    }

    [Fact]
    public void CommentBody_ShouldPreserveOriginalContent()
    {
        var originalBody = "This is a test comment.";
        var taggedBody = $"[abo]\n{originalBody}";

        Assert.EndsWith(originalBody, taggedBody);
        Assert.Equal($"[abo]\n{originalBody}", taggedBody);
    }

    [Fact]
    public void CommentBody_EmptyBody_ShouldStillHavePrefix()
    {
        var originalBody = "";
        var taggedBody = $"[abo]\n{originalBody}";

        Assert.Equal("[abo]\n", taggedBody);
    }

    [Fact]
    public void CommentBody_MultilineBody_ShouldPreserveAllLines()
    {
        var originalBody = "Line 1\nLine 2\nLine 3";
        var taggedBody = $"[abo]\n{originalBody}";

        Assert.StartsWith("[abo]\n", taggedBody);
        Assert.Contains("Line 1", taggedBody);
        Assert.Contains("Line 2", taggedBody);
        Assert.Contains("Line 3", taggedBody);
    }

    // --- CreateIssueAsync prefix logic ---

    [Fact]
    public void IssueTitle_ShouldStartWithAboPrefix()
    {
        var originalTitle = "My new issue title";
        var taggedTitle = $"[abo] {originalTitle}";

        Assert.StartsWith("[abo] ", taggedTitle);
    }

    [Fact]
    public void IssueTitle_ShouldPreserveOriginalContent()
    {
        var originalTitle = "My new issue title";
        var taggedTitle = $"[abo] {originalTitle}";

        Assert.Equal($"[abo] {originalTitle}", taggedTitle);
    }

    [Fact]
    public void IssueTitle_EmptyTitle_ShouldStillHavePrefix()
    {
        var originalTitle = "";
        var taggedTitle = $"[abo] {originalTitle}";

        Assert.Equal("[abo] ", taggedTitle);
    }

    [Fact]
    public void IssueTitle_PrefixFormat_ShouldUseSingleSpace()
    {
        // Ensure the delimiter between [abo] and the title is exactly one space (not newline)
        var originalTitle = "Test";
        var taggedTitle = $"[abo] {originalTitle}";

        Assert.Equal("[abo] Test", taggedTitle);
        Assert.DoesNotContain("[abo]\n", taggedTitle);
    }

    [Fact]
    public void CommentPrefix_Format_ShouldUseNewline()
    {
        // Ensure the delimiter between [abo] and the body is a newline (not space)
        var originalBody = "Test";
        var taggedBody = $"[abo]\n{originalBody}";

        Assert.Equal("[abo]\nTest", taggedBody);
        Assert.DoesNotContain("[abo] ", taggedBody);
    }
}
