using Api.Extensions;
using Application.Common.Models;
using Application.Features.Sources.GetSources;
using FastEndpoints;
using MediatR;

namespace Api.Endpoints.Sources;

/// <summary>GET /sources — list all ingested source files grouped by file name.</summary>
public sealed class GetSourcesEndpoint(ISender sender)
    : EndpointWithoutRequest<ApiResponse<GetSourcesResponse>>
{
    public override void Configure()
    {
        Get("/sources");
        AllowAnonymous();
        Tags("Sources");
        Summary(s =>
        {
            s.Summary = "List ingested source files";
            s.Description = "Returns all ingested PDF source files grouped by file name, "
                + "including the number of stored pages and all associated tags.";
            s.Response<ApiResponse<GetSourcesResponse>>(200, "Sources listed");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await sender.Send(new GetSourcesQuery(), ct);
        await Send.ResponseAsync(result.ToApiResponse(), result.ToHttpStatusCode(), ct);
    }
}
