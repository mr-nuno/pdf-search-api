namespace Domain.Documents;

/// <summary>
/// A single page of an ingested PDF, stored as an independent RavenDB document so that
/// search hits can be targeted to a precise page.
/// </summary>
/// <remarks>
/// This is a plain data holder (read-mostly stored data), not an aggregate root — public
/// setters are intentional and required for RavenDB deserialization. Ids are Raven strings
/// assigned by the store via the <c>DocumentPages/</c> collection prefix.
/// </remarks>
public sealed class DocumentPage
{
    public const string DefaultTag = "none";

    public string Id { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public int PageNumber { get; set; }

    /// <summary>The running-header text for the page (e.g. a chapter title), or <c>null</c>.</summary>
    public string? Header { get; set; }

    /// <summary>The printed page number, separate from <see cref="PageNumber"/>, or <c>null</c>.</summary>
    public string? PageLabel { get; set; }

    /// <summary>The page body as markdown, excluding header/footer and page number.</summary>
    public string Content { get; set; } = string.Empty;

    public DateTimeOffset IngestedAt { get; set; }

    public string Tag { get; set; } = DefaultTag;
}
