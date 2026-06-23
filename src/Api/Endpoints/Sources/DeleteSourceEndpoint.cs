using Api.Extensions;
using Application.Common.Models;
using Application.Features.Sources.DeleteSource;
using FastEndpoints;
using MediatR;

namespace Api.Endpoints.Sources;

/// <summary>DELETE /sources?sourceFileName= — remove all pages for a given source file.</summary>
public sealed class DeleteSourceEndpoint(ISender sender)
    : Endpoint<DeleteSourceRequest, ApiResponse<DeleteSourceResponse>>
{
    public override void Configure()
    {
        Delete("/sources");
        AllowAnonymous();
        Tags("Sources");
        Summary(s =>
        {
            s.Summary = "Delete an ingested source file";
            s.Description = "Deletes all stored pages for the specified source file. "
                + "Returns the count of deleted pages and any tags that are no longer present on any remaining document.";
            s.Params["sourceFileName"] = "The source file name to delete (must match the name used during ingestion).";
            s.Response<ApiResponse<DeleteSourceResponse>>(200, "Source deleted");
            s.Response<ApiResponse<DeleteSourceResponse>>(404, "Source file not found");
        });
    }

    public override async Task HandleAsync(DeleteSourceRequest req, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteSourceCommand(req.SourceFileName), ct);
        await Send.ResponseAsync(result.ToApiResponse(), result.ToHttpStatusCode(), ct);
    }
}
