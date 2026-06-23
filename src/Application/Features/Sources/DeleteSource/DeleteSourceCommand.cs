using Application.Common.Interfaces;
using Ardalis.Result;
using FluentValidation;
using MediatR;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Serilog;

namespace Application.Features.Sources.DeleteSource;

public sealed record DeleteSourceCommand(string SourceFileName) : IRequest<Result<DeleteSourceResponse>>
{
    public sealed class Handler(IApplicationDbContext db)
        : IRequestHandler<DeleteSourceCommand, Result<DeleteSourceResponse>>
    {
        private static readonly ILogger Log = Serilog.Log.ForContext<Handler>();

        public async Task<Result<DeleteSourceResponse>> Handle(DeleteSourceCommand request, CancellationToken ct)
        {
            var pages = await db.DocumentPages
                .Where(x => x.SourceFileName == request.SourceFileName)
                .Take(4096)
                .ToListAsync(ct);

            if (pages.Count == 0)
                return Result.NotFound($"No documents found for source file '{request.SourceFileName}'.");

            var sourceTags = pages.SelectMany(p => p.Tags).Distinct().ToHashSet();

            // Determine which tags no longer appear in any remaining source after this delete
            var tagsRemoved = new List<string>();
            if (sourceTags.Count > 0)
            {
                var pagesWithOverlappingTags = await db.DocumentPages
                    .Where(x => x.SourceFileName != request.SourceFileName)
                    .Where(x => x.Tags.ContainsAny(sourceTags.ToList()))
                    .Take(4096)
                    .ToListAsync(ct);

                var tagsStillInUse = pagesWithOverlappingTags
                    .SelectMany(p => p.Tags)
                    .Intersect(sourceTags)
                    .ToHashSet();

                tagsRemoved = sourceTags.Except(tagsStillInUse).OrderBy(t => t).ToList();
            }

            foreach (var page in pages)
                db.Delete(page);

            await db.SaveChangesAsync(ct);

            Log.Information("Deleted {PagesDeleted} page(s) for {SourceFileName}", pages.Count, request.SourceFileName);
            return Result.Success(new DeleteSourceResponse(request.SourceFileName, pages.Count, tagsRemoved));
        }
    }

    public sealed class Validator : AbstractValidator<DeleteSourceCommand>
    {
        public Validator()
        {
            RuleFor(x => x.SourceFileName).NotEmpty().WithMessage("sourceFileName is required.");
        }
    }
}
