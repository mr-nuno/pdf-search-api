using FastEndpoints;

namespace Api.Endpoints.Search;

/// <summary>Query string for <c>GET /search</c>. The property is bound from <c>?query=</c>.</summary>
public sealed class SearchPagesRequest
{
    [QueryParam, BindFrom("query")]
    public string? Query { get; set; }

    [QueryParam, BindFrom("tags")]
    public List<string>? Tags { get; set; }
}
