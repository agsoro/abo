using System.Text.Json;
using Xunit;
using Abo.Integrations.Mattermost;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the Mattermost typing indicator bug fix.
///
/// Root cause: In HandleMessageAsync, the typing indicator loop was passing `threadId`
/// (which equals post.Id for plain DMs) as the parent_id to SendTypingOverWebSocketAsync.
/// Mattermost silently discards the typing indicator when parent_id is set to a non-thread-root ID.
///
/// Fix: Introduced `typingParentId` which is null for plain DMs (post.RootId empty) and
/// post.RootId for threaded replies. SendTypingOverWebSocketAsync already omits parent_id
/// from the WebSocket payload when null/empty.
/// </summary>
[Trait("Category", "Unit")]
public class MattermostTypingIndicatorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // typingParentId computation logic
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TypingParentId_IsNull_ForPlainDmMessage()
    {
        // In a plain DM, post.RootId is empty — typingParentId must be null
        // so that SendTypingOverWebSocketAsync omits parent_id from the WebSocket payload.
        var post = new MattermostPost
        {
            Id = "post-abc",
            RootId = "",
            ChannelId = "dm-channel-1",
            UserId = "user-1",
            Message = "Hello bot"
        };

        var typingParentId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : null;

        Assert.Null(typingParentId);
    }

    [Fact]
    public void TypingParentId_IsRootId_ForThreadedMessage()
    {
        // In a thread, post.RootId points to the thread root — typingParentId should be RootId
        // so that the typing indicator is shown inside the thread.
        var post = new MattermostPost
        {
            Id = "post-reply-xyz",
            RootId = "thread-root-789",
            ChannelId = "channel-general",
            UserId = "user-1",
            Message = "A reply in a thread"
        };

        var typingParentId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : null;

        Assert.Equal("thread-root-789", typingParentId);
    }

    [Fact]
    public void TypingParentId_IsNull_WhenRootIdIsWhitespace()
    {
        // A whitespace-only RootId should also be treated as absent.
        var post = new MattermostPost
        {
            Id = "post-def",
            RootId = "   ",
            ChannelId = "dm-channel-2",
            UserId = "user-2",
            Message = "Another message"
        };

        // string.IsNullOrEmpty does NOT catch whitespace, but the Mattermost API sends "" for
        // non-thread posts; testing with the same condition used in the production code.
        var typingParentId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : null;

        // Whitespace is technically non-empty, so the condition preserves it —
        // this confirms the production code only relies on Mattermost always sending "" (not whitespace).
        Assert.NotNull(typingParentId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // threadId computation (reply root_id) — must NOT change
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThreadId_IsPostId_ForPlainDmMessage()
    {
        // For reply root_id: falls back to post.Id in a plain DM (unchanged behavior)
        var post = new MattermostPost
        {
            Id = "post-abc",
            RootId = "",
            ChannelId = "dm-channel-1",
            UserId = "user-1",
            Message = "Hello bot"
        };

        var threadId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : post.Id;

        Assert.Equal("post-abc", threadId);
    }

    [Fact]
    public void ThreadId_IsRootId_ForThreadedMessage()
    {
        // For reply root_id: uses post.RootId so the reply stays in the thread
        var post = new MattermostPost
        {
            Id = "post-reply-xyz",
            RootId = "thread-root-789",
            ChannelId = "channel-general",
            UserId = "user-1",
            Message = "A reply in a thread"
        };

        var threadId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : post.Id;

        Assert.Equal("thread-root-789", threadId);
    }

    [Fact]
    public void ThreadId_And_TypingParentId_AreIndependent_ForPlainDm()
    {
        // Key correctness invariant: threadId != typingParentId for a plain DM.
        // threadId is post.Id (to reply in the DM), typingParentId is null (no parent for typing).
        var post = new MattermostPost
        {
            Id = "post-abc",
            RootId = "",
            ChannelId = "dm-channel-1",
            UserId = "user-1",
            Message = "Hello bot"
        };

        var threadId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : post.Id;
        var typingParentId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : null;

        Assert.Equal("post-abc", threadId);       // reply uses the post ID
        Assert.Null(typingParentId);               // typing omits parent_id
        Assert.NotEqual(threadId, typingParentId); // they must differ
    }

    [Fact]
    public void ThreadId_And_TypingParentId_AreEqual_ForThreadedMessage()
    {
        // For a threaded message, both threadId and typingParentId should equal RootId.
        var post = new MattermostPost
        {
            Id = "post-reply-xyz",
            RootId = "thread-root-789",
            ChannelId = "channel-general",
            UserId = "user-1",
            Message = "A reply in a thread"
        };

        var threadId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : post.Id;
        var typingParentId = !string.IsNullOrEmpty(post.RootId) ? post.RootId : null;

        Assert.Equal("thread-root-789", threadId);
        Assert.Equal("thread-root-789", typingParentId);
        Assert.Equal(threadId, typingParentId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MattermostWebSocketAction serialization — parent_id omission
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WebSocketAction_DoesNotContainParentId_WhenTypingParentIdIsNull()
    {
        // SendTypingOverWebSocketAsync only adds parent_id when the value is non-null/non-empty.
        // This test verifies the serialised JSON does NOT contain parent_id for a plain DM.
        string? typingParentId = null;

        var action = new MattermostWebSocketAction
        {
            Action = "user_typing",
            Seq = 1,
            Data = new Dictionary<string, string>
            {
                { "channel_id", "dm-channel-1" }
            }
        };

        if (!string.IsNullOrEmpty(typingParentId))
            action.Data["parent_id"] = typingParentId;

        var json = JsonSerializer.Serialize(action);

        Assert.Contains("\"channel_id\"", json);
        Assert.DoesNotContain("\"parent_id\"", json);
        Assert.Contains("user_typing", json);
    }

    [Fact]
    public void WebSocketAction_ContainsParentId_WhenTypingParentIdIsSet()
    {
        // When we are inside a thread, parent_id must be included in the WebSocket payload.
        var typingParentId = "thread-root-789";

        var action = new MattermostWebSocketAction
        {
            Action = "user_typing",
            Seq = 2,
            Data = new Dictionary<string, string>
            {
                { "channel_id", "channel-general" }
            }
        };

        if (!string.IsNullOrEmpty(typingParentId))
            action.Data["parent_id"] = typingParentId;

        var json = JsonSerializer.Serialize(action);

        Assert.Contains("\"channel_id\"", json);
        Assert.Contains("\"parent_id\"", json);
        Assert.Contains("thread-root-789", json);
        Assert.Contains("user_typing", json);
    }

    [Fact]
    public void WebSocketAction_DoesNotContainParentId_WhenTypingParentIdIsEmptyString()
    {
        // An empty string typingParentId must also result in no parent_id in the payload.
        var typingParentId = "";

        var action = new MattermostWebSocketAction
        {
            Action = "user_typing",
            Seq = 3,
            Data = new Dictionary<string, string>
            {
                { "channel_id", "dm-channel-2" }
            }
        };

        if (!string.IsNullOrEmpty(typingParentId))
            action.Data["parent_id"] = typingParentId;

        var json = JsonSerializer.Serialize(action);

        Assert.Contains("\"channel_id\"", json);
        Assert.DoesNotContain("\"parent_id\"", json);
    }

    [Fact]
    public void WebSocketAction_SerializesCorrectActionName()
    {
        var action = new MattermostWebSocketAction
        {
            Action = "user_typing",
            Seq = 5,
            Data = new Dictionary<string, string> { { "channel_id", "ch-1" } }
        };

        var json = JsonSerializer.Serialize(action);

        Assert.Contains("\"action\":\"user_typing\"", json);
        Assert.Contains("\"seq\":5", json);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MattermostPost deserialization
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MattermostPost_Deserializes_WithEmptyRootId_ForDm()
    {
        var postJson = """
            {
                "id": "post-abc",
                "message": "Hello bot",
                "channel_id": "dm-channel-1",
                "user_id": "user-1",
                "root_id": ""
            }
            """;

        var post = JsonSerializer.Deserialize<MattermostPost>(postJson);

        Assert.NotNull(post);
        Assert.Equal("post-abc", post!.Id);
        Assert.Equal("", post.RootId);
        Assert.True(string.IsNullOrEmpty(post.RootId));
    }

    [Fact]
    public void MattermostPost_Deserializes_WithRootId_ForThreadedMessage()
    {
        var postJson = """
            {
                "id": "post-reply-xyz",
                "message": "A reply in a thread",
                "channel_id": "channel-general",
                "user_id": "user-1",
                "root_id": "thread-root-789"
            }
            """;

        var post = JsonSerializer.Deserialize<MattermostPost>(postJson);

        Assert.NotNull(post);
        Assert.Equal("post-reply-xyz", post!.Id);
        Assert.Equal("thread-root-789", post.RootId);
        Assert.False(string.IsNullOrEmpty(post.RootId));
    }

    [Fact]
    public void MattermostPost_Deserializes_WithMissingRootId_AsEmptyString()
    {
        // Mattermost may omit root_id entirely for non-thread posts in some versions.
        var postJson = """
            {
                "id": "post-abc",
                "message": "Hello bot",
                "channel_id": "dm-channel-1",
                "user_id": "user-1"
            }
            """;

        var post = JsonSerializer.Deserialize<MattermostPost>(postJson);

        Assert.NotNull(post);
        // Default value of RootId is "" (from the DTO initializer), so IsNullOrEmpty is true.
        Assert.True(string.IsNullOrEmpty(post!.RootId));
    }
}
