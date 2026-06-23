namespace Application.Features.Search.SearchPages;

public sealed record SearchResultDto(
    string SourceFileName,
    int PageNumber,
    string Content,
    double SearchScore);

public sealed record SearchResponseDto(
    string Query,
    int TotalHits,
    List<SearchResultDto> Results);
