using Api.Auth;
using Api.Extensions;
using Application.Common.Models;
using Application.Features.Ingestion.IngestDocument;
using FastEndpoints;
using MediatR;

namespace Api.Endpoints.Documents;

/// <summary>POST /documents — ingest a PDF, storing each non-empty page as a searchable document.</summary>
public sealed class IngestDocumentEndpoint(ISender sender)
    : Endpoint<IngestDocumentRequest, ApiResponse<IngestDocumentResponse>>
{
    public override void Configure()
    {
        Post("/documents");
        Policies(AuthPolicy.Write);
        AllowFileUploads();
        Tags("Documents");
        Summary(s =>
        {
            s.Summary = "Ingest a PDF document";
            s.Description = "Extracts text page-by-page from the uploaded PDF and stores each non-empty "
                + "page as an independent document in RavenDB for full-text search.";
            s.Params["tags"] = "Optional tags to categorize the ingested document (repeat the field for multiple tags, stored lowercase).";
            s.Response<ApiResponse<IngestDocumentResponse>>(201, "Document ingested");
            s.Response<ApiResponse<IngestDocumentResponse>>(400, "Invalid or empty file");
        });
    }

    public override async Task HandleAsync(IngestDocumentRequest req, CancellationToken ct)
    {
        await using var stream = req.File.OpenReadStream();

        var result = await sender.Send(new IngestDocumentCommand(stream, req.File.FileName, req.Tags), ct);
        await Send.ResponseAsync(result.ToApiResponse(), result.IsSuccess ? 201 : result.ToHttpStatusCode(), ct);
    }
}
