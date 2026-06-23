using Application.Common.Abstractions;
using Application.Common.Interfaces;
using Ardalis.Result;
using Domain.Documents;
using FluentValidation;
using MediatR;
using Serilog;

namespace Application.Features.Ingestion.IngestDocument;

public sealed record IngestDocumentCommand(Stream Content, string FileName)
    : IRequest<Result<IngestDocumentResponse>>
{
    public sealed class Handler(
        IApplicationDbContext db,
        IPdfTextExtractor extractor,
        IDateTimeProvider dateTime) : IRequestHandler<IngestDocumentCommand, Result<IngestDocumentResponse>>
    {
        private static readonly ILogger Log = Serilog.Log.ForContext<Handler>();

        public async Task<Result<IngestDocumentResponse>> Handle(IngestDocumentCommand request, CancellationToken ct)
        {
            int pagesIngested = 0;

            foreach (PdfPageText page in extractor.Extract(request.Content))
            {
                var documentPage = new DocumentPage
                {
                    SourceFileName = request.FileName,
                    PageNumber = page.PageNumber,
                    Content = page.Text,
                    IngestedAt = dateTime.UtcNow
                };

                await db.StoreAsync(documentPage, ct);
                pagesIngested++;
            }

            await db.SaveChangesAsync(ct);

            Log.Information("Ingested {PagesIngested} page(s) from {FileName}", pagesIngested, request.FileName);
            return Result.Success(new IngestDocumentResponse(request.FileName, pagesIngested));
        }
    }

    public sealed class Validator : AbstractValidator<IngestDocumentCommand>
    {
        public Validator()
        {
            RuleFor(x => x.FileName)
                .NotEmpty().WithMessage("A file name is required.");

            RuleFor(x => x.Content)
                .NotNull().WithMessage("PDF content is required.");
        }
    }
}
