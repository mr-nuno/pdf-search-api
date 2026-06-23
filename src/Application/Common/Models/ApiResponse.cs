namespace Application.Common.Models;

/// <summary>Consistent response envelope returned by every endpoint — never a raw DTO.</summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }
    public List<ValidationError>? ValidationErrors { get; init; }
}

public sealed record ValidationError(string Property, string Message);
