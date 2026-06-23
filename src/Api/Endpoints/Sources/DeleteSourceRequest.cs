using FastEndpoints;
using FluentValidation;

namespace Api.Endpoints.Sources;

public sealed class DeleteSourceRequest
{
    [QueryParam, BindFrom("sourceFileName")]
    public string SourceFileName { get; set; } = string.Empty;
}

public sealed class DeleteSourceRequestValidator : Validator<DeleteSourceRequest>
{
    public DeleteSourceRequestValidator()
    {
        RuleFor(x => x.SourceFileName)
            .NotEmpty().WithMessage("sourceFileName query parameter is required.");
    }
}
