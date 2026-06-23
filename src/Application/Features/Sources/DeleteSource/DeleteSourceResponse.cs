namespace Application.Features.Sources.DeleteSource;

public sealed record DeleteSourceResponse(string SourceFileName, int PagesDeleted, List<string> TagsRemoved);
