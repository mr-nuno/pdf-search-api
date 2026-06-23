using FastEndpoints;
using FluentValidation;

namespace Api.Endpoints.Documents;

public sealed class IngestDocumentRequest
{
    public IFormFile File { get; set; } = default!;

    [BindFrom("tags")]
    public List<string>? Tags { get; set; }
}

/// <summary>HTTP-level validation for the upload (file present, .pdf, non-empty).</summary>
public sealed class IngestDocumentRequestValidator : Validator<IngestDocumentRequest>
{
    public IngestDocumentRequestValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("A PDF file is required.");

        RuleFor(x => x.File.FileName)
            .Must(name => name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .When(x => x.File is not null)
            .WithMessage("Only .pdf files are supported.");

        RuleFor(x => x.File.Length)
            .GreaterThan(0)
            .When(x => x.File is not null)
            .WithMessage("The PDF file is empty.");
    }
}
