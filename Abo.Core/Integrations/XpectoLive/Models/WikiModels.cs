using System.Text.Json.Serialization;

namespace Abo.Integrations.XpectoLive.Models;

public record SpacePageInfo
{
    [JsonPropertyName("pageID")]
    public string? PageID { get; init; }

    [JsonPropertyName("pageTitle")]
    public string? PageTitle { get; init; }

    [JsonPropertyName("actor")]
    public string? Actor { get; init; }

    [JsonPropertyName("actionTimestamp")]
    public DateTime? ActionTimestamp { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("entryType")]
    public string? EntryType { get; init; }
}

public record SpaceInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("entity")]
    public string? Entity { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("acl")]
    public string? Acl { get; init; }

    [JsonPropertyName("created")]
    public DateTime? Created { get; init; }

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("spacePage")]
    public string? SpacePage { get; init; }

    [JsonPropertyName("spacePageTitle")]
    public string? SpacePageTitle { get; init; }
}

public record Space
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("entity")]
    public string? Entity { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("acl")]
    public string? Acl { get; init; }

    [JsonPropertyName("created")]
    public DateTime? Created { get; init; }

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("spacePage")]
    public string? SpacePage { get; init; }

    [JsonPropertyName("spacePageTitle")]
    public string? SpacePageTitle { get; init; }

    [JsonPropertyName("startPage")]
    public clWikiTree? StartPage { get; init; }
}

public record SpaceNew
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

public record SpaceUpdate
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("acl")]
    public string? Acl { get; init; }
}

public record Page
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("editorState")]
    public byte[]? EditorState { get; init; }

    [JsonPropertyName("versionComment")]
    public string? VersionComment { get; init; }

    [JsonPropertyName("versionContributors")]
    public string? VersionContributors { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("attachments")]
    public Attachment[]? Attachments { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("acl")]
    public string? Acl { get; init; }

    [JsonPropertyName("versionNo")]
    public int? VersionNo { get; init; }

    [JsonPropertyName("sort")]
    public int? Sort { get; init; }

    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("entity")]
    public string? Entity { get; init; }
}

public record PageNew
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }
}

public record PageUpdate
{
    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    [JsonPropertyName("sort")]
    public int? Sort { get; init; }

    [JsonPropertyName("acl")]
    public string? Acl { get; init; }
}

public record ContentUpdate
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("editorState")]
    public byte[]? EditorState { get; init; }

    [JsonPropertyName("versionComment")]
    public string? VersionComment { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }
}

public record MovePageRequest
{
    [JsonPropertyName("targetSpaceId")]
    public string? TargetSpaceId { get; init; }

    [JsonPropertyName("targetParentId")]
    public string? TargetParentId { get; init; }
}

public record CopyPageRequest
{
    [JsonPropertyName("targetSpaceId")]
    public string? TargetSpaceId { get; init; }

    [JsonPropertyName("targetParentId")]
    public string? TargetParentId { get; init; }

    [JsonPropertyName("includeChildren")]
    public bool IncludeChildren { get; init; }

    [JsonPropertyName("includeAttachments")]
    public bool IncludeAttachments { get; init; }
}

public record clWikiTree
{
    [JsonPropertyName("level")]
    public int? Level { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("slug")]
    public string? Slug { get; init; }

    [JsonPropertyName("acl")]
    public string? Acl { get; init; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    [JsonPropertyName("parentTitle")]
    public string? ParentTitle { get; init; }

    [JsonPropertyName("versionComment")]
    public string? VersionComment { get; init; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; init; }

    [JsonPropertyName("childs")]
    public clWikiTree[]? Childs { get; init; }
}

public record Attachment
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }
    
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }
    
    [JsonPropertyName("contentType")]
    public string? ContentType { get; init; }
    
    [JsonPropertyName("size")]
    public long? Size { get; init; }
}
