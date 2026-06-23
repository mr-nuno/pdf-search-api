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
        AllowAnonymous();
        AllowFileUploads();
        Tags("Documents");
        Summary(s =>
        {
            s.Summary = "Ingest a PDF document";
            s.Description = "Extracts text page-by-page from the uploaded PDF and stores each non-empty "
                + "page as an independent document in RavenDB for full-text search.";
            s.Response<ApiResponse<IngestDocumentResponse>>(201, "Document ingested");
            s.Response<ApiResponse<IngestDocumentResponse>>(400, "Invalid or empty file");
        });
    }

    public override async Task HandleAsync(IngestDocumentRequest req, CancellationToken ct)
    {
        await using Stream stream = req.File.OpenReadStream();

        var result = await sender.Send(new IngestDocumentCommand(stream, req.File.FileName), ct);
        await Send.ResponseAsync(result.ToApiResponse(), result.IsSuccess ? 201 : result.ToHttpStatusCode(), ct);
    }
}
