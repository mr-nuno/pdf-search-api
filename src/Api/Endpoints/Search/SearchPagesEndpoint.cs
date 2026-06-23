using Api.Extensions;
using Application.Common.Models;
using Application.Features.Search.SearchPages;
using FastEndpoints;
using MediatR;

namespace Api.Endpoints.Search;

/// <summary>GET /search?query= — full-text search across ingested PDF pages.</summary>
public sealed class SearchPagesEndpoint(ISender sender)
    : Endpoint<SearchPagesRequest, ApiResponse<SearchResponseDto>>
{
    public override void Configure()
    {
        Get("/search");
        AllowAnonymous();
        Tags("Search");
        Summary(s =>
        {
            s.Summary = "Full-text search across ingested PDF pages";
            s.Description = "Searches page content via the DocumentPages/Search index and returns matched "
                + "pages with their source file name, page number, and relevance score.";
            s.Params["query"] = "The full-text search term (required).";
            s.Params["tag"] = "An optional tag to filter the search results.";
            s.Response<ApiResponse<SearchResponseDto>>(200, "Search completed");
            s.Response<ApiResponse<SearchResponseDto>>(400, "Query was missing or empty");
        });
    }

    public override async Task HandleAsync(SearchPagesRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new SearchPagesQuery(req.Query ?? string.Empty, req.Tag), ct);
        await Send.ResponseAsync(result.ToApiResponse(), result.ToHttpStatusCode(), ct);
    }
}
