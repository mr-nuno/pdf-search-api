using Application.Common.Interfaces;
using Ardalis.Result;
using Domain.Documents;
using FluentValidation;
using MediatR;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Serilog;

namespace Application.Features.Search.SearchPages;

public sealed record SearchPagesQuery(string Query) : IRequest<Result<SearchResponseDto>>
{
    public sealed class Handler(IApplicationDbContext db)
        : IRequestHandler<SearchPagesQuery, Result<SearchResponseDto>>
    {
        private const int MaxResults = 25;
        private static readonly ILogger Log = Serilog.Log.ForContext<Handler>();

        public async Task<Result<SearchResponseDto>> Handle(SearchPagesQuery request, CancellationToken ct)
        {
            List<DocumentPage> pages = await db.DocumentPages
                .Statistics(out QueryStatistics stats)
                .Search(x => x.Content, request.Query)
                .Search(x => x.Header, request.Query)
                .Take(MaxResults)
                .ToListAsync(ct);

            List<SearchResultDto> results = pages
                .Select(page => new SearchResultDto(
                    page.SourceFileName,
                    page.PageNumber,
                    page.Header,
                    page.PageLabel,
                    page.Content,
                    db.IndexScore(page)))
                .ToList();

            Log.Information("Search for {Query} matched {TotalHits} page(s)", request.Query, stats.TotalResults);
            return Result.Success(new SearchResponseDto(request.Query, (int)stats.TotalResults, results));
        }
    }

    public sealed class Validator : AbstractValidator<SearchPagesQuery>
    {
        public Validator()
        {
            RuleFor(x => x.Query)
                .NotEmpty().WithMessage("A search query is required.");
        }
    }
}
