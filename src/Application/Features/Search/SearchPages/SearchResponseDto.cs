namespace Application.Features.Search.SearchPages;

public sealed record SearchResultDto(
    string SourceFileName,
    int PageNumber,
    string? Header,
    string? PageLabel,
    string Content,
    string Tag,
    double SearchScore);

public sealed record SearchResponseDto(
    string Query,
    int TotalHits,
    List<SearchResultDto> Results);
