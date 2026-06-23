using Application.Common.Interfaces;
using Ardalis.Result;
using MediatR;
using Raven.Client.Documents;

namespace Application.Features.Sources.GetSources;

public sealed record GetSourcesQuery : IRequest<Result<GetSourcesResponse>>
{
    public sealed class Handler(IApplicationDbContext db)
        : IRequestHandler<GetSourcesQuery, Result<GetSourcesResponse>>
    {
        public async Task<Result<GetSourcesResponse>> Handle(GetSourcesQuery request, CancellationToken ct)
        {
            var pages = await db.DocumentPages
                .Take(2048)
                .ToListAsync(ct);

            var sources = pages
                .GroupBy(p => p.SourceFileName)
                .Select(g => new SourceDto(
                    g.Key,
                    g.Count(),
                    g.SelectMany(p => p.Tags).Distinct().OrderBy(t => t).ToList()))
                .OrderBy(s => s.SourceFileName)
                .ToList();

            return Result.Success(new GetSourcesResponse(sources));
        }
    }
}
