namespace Application.Features.Search.SearchPages;

public sealed record SearchResultDto(
    string SourceFileName,
    int PhysicalPageNumber,
    string? Header,
    string Content,
    List<string> Tags,
    double SearchScore);

public sealed record SearchResponseDto(
    string Query,
    int TotalHits,
    List<SearchResultDto> Results);
