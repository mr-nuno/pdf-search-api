using Application.Common.Interfaces;
using Ardalis.Result;
using Domain.Documents;
using FluentValidation;
using MediatR;
using Raven.Client.Documents;
using Serilog;

namespace Application.Features.Ingestion.IngestDocument;

public sealed record IngestDocumentCommand(Stream Content, string FileName, List<string>? Tags, bool Replace = false)
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
            if (request.Replace)
            {
                var existing = await db.DocumentPages
                    .Where(x => x.SourceFileName == request.FileName)
                    .Take(4096)
                    .ToListAsync(ct);

                foreach (var page in existing)
                    db.Delete(page);
            }

            var pagesIngested = 0;

            var tags = request.Tags is { Count: > 0 }
                ? request.Tags
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => t.Length > 0)
                    .Distinct()
                    .ToList()
                : [DocumentPage.DefaultTag];

            foreach (var page in extractor.Extract(request.Content))
            {
                var documentPage = new DocumentPage
                {
                    SourceFileName = request.FileName,
                    PageNumber = page.PageNumber,
                    Header = page.Header,
                    Content = page.Content,
                    IngestedAt = dateTime.UtcNow,
                    Tags = tags
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
