using System.Text.RegularExpressions;
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

public sealed record SearchPagesQuery(string Query, List<string>? Tags) : IRequest<Result<SearchResponseDto>>
{
    public sealed class Handler(IApplicationDbContext db)
        : IRequestHandler<SearchPagesQuery, Result<SearchResponseDto>>
    {
        private const int MaxResults = 25;
        private static readonly ILogger Log = Serilog.Log.ForContext<Handler>();

        public async Task<Result<SearchResponseDto>> Handle(SearchPagesQuery request, CancellationToken ct)
        {
            var keywordQuery = SwedishQueryPreprocessor.Process(request.Query);
            var tokens = keywordQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Header matches are strongly boosted: a match in the chapter title is far more
            // indicative than a match buried in body text.
            var query = db.DocumentPages
                .Statistics(out var stats)
                .Search(x => x.Header,  keywordQuery, boost: 8m)
                .Search(x => x.Content, keywordQuery);

            // For multi-word queries, also search for the exact phrase (Lucene phrase syntax).
            // A phrase match is rewarded above scattered keyword matches.
            if (tokens.Length > 1)
            {
                var phrase = $"\"{keywordQuery}\"";
                query = query
                    .Search(x => x.Header,  phrase, boost: 20m)
                    .Search(x => x.Content, phrase, boost: 10m);
            }

            if (request.Tags is { Count: > 0 })
            {
                var normalizedTags = request.Tags
                    .Select(t => t.Trim().ToLowerInvariant())
                    .Where(t => t.Length > 0)
                    .ToList();
                if (normalizedTags.Count > 0)
                    query = query.Where(x => x.Tags.ContainsAny(normalizedTags));
            }

            var pages = await query
                .Take(MaxResults)
                .ToListAsync(ct);

            var results = pages
                .Select(page =>
                {
                    var content = BoldMatches(TrimToFirstMatch(page.Content, keywordQuery), keywordQuery);
                    return new SearchResultDto(
                        page.SourceFileName,
                        page.PageNumber,
                        page.Header,
                        content,
                        page.Tags,
                        db.IndexScore(page));
                })
                .ToList();

            Log.Information("Search for {Query} (processed: {ProcessedQuery}) matched {TotalHits} page(s)",
                request.Query, keywordQuery, stats.TotalResults);
            return Result.Success(new SearchResponseDto(request.Query, keywordQuery, (int)stats.TotalResults, results));
        }

        private static string TrimToFirstMatch(string content, string query)
        {
            var firstIdx = query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => content.IndexOf(t, StringComparison.OrdinalIgnoreCase))
                .Where(i => i >= 0)
                .Append(int.MaxValue)
                .Min();

            if (firstIdx == int.MaxValue)
                return content;

            for (var i = firstIdx - 1; i >= 1; i--)
            {
                if (content[i] == '\n' && content[i - 1] == '\n')
                    return content[(i + 1)..];

                if ((content[i] == ' ' || content[i] == '\n') &&
                    (content[i - 1] == '.' || content[i - 1] == '!' || content[i - 1] == '?'))
                    return content[(i + 1)..];
            }

            return content;
        }

        private static string BoldMatches(string content, string query)
        {
            foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                content = Regex.Replace(content, Regex.Escape(token), m => $"**{m.Value}**", RegexOptions.IgnoreCase);
            return content;
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
