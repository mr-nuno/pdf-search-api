namespace Application.Features.Sources.GetSources;

public sealed record SourceDto(string SourceFileName, int PageCount, List<string> Tags);

public sealed record GetSourcesResponse(List<SourceDto> Sources);
