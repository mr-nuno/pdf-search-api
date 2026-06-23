using Application.Common.Interfaces;
using Ardalis.Result;
using Domain.Documents;
using FluentValidation;
using MediatR;
using Serilog;

namespace Application.Features.Ingestion.IngestDocument;

public sealed record IngestDocumentCommand(Stream Content, string FileName, string? Tag)
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
            var pagesIngested = 0;

            var tag = string.IsNullOrWhiteSpace(request.Tag) ? DocumentPage.DefaultTag : request.Tag;

            foreach (var page in extractor.Extract(request.Content))
            {
                var documentPage = new DocumentPage
                {
                    SourceFileName = request.FileName,
                    PageNumber = page.PageNumber,
                    Header = page.Header,
                    PageLabel = page.PageLabel,
                    Content = page.Content,
                    IngestedAt = dateTime.UtcNow,
                    Tag = tag
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
