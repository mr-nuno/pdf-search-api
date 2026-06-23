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
    public string Id { get; set; } = string.Empty;

    public string SourceFileName { get; set; } = string.Empty;

    public int PageNumber { get; set; }

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset IngestedAt { get; set; }
}
