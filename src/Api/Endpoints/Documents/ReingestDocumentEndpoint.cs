using Api.Auth;
using Api.Extensions;
using Application.Common.Models;
using Application.Features.Ingestion.IngestDocument;
using FastEndpoints;
using MediatR;

namespace Api.Endpoints.Documents;

/// <summary>PUT /documents — replace an existing ingested PDF with a new version.</summary>
public sealed class ReingestDocumentEndpoint(ISender sender)
    : Endpoint<IngestDocumentRequest, ApiResponse<IngestDocumentResponse>>
{
    public override void Configure()
    {
        Put("/documents");
        Policies(AuthPolicy.Write);
        AllowFileUploads();
        Tags("Documents");
        Summary(s =>
        {
            s.Summary = "Replace an ingested PDF document";
            s.Description = "Deletes all previously stored pages for the uploaded file name, then re-ingests "
                + "the new version page-by-page. Use this to update a document that has already been ingested.";
            s.Params["tags"] = "Optional tags to categorize the re-ingested document (repeat the field for multiple tags, stored lowercase).";
            s.Response<ApiResponse<IngestDocumentResponse>>(200, "Document replaced");
            s.Response<ApiResponse<IngestDocumentResponse>>(400, "Invalid or empty file");
        });
    }

    public override async Task HandleAsync(IngestDocumentRequest req, CancellationToken ct)
    {
        await using var stream = req.File.OpenReadStream();

        var result = await sender.Send(new IngestDocumentCommand(stream, req.File.FileName, req.Tags, Replace: true), ct);
        await Send.ResponseAsync(result.ToApiResponse(), result.ToHttpStatusCode(), ct);
    }
}
