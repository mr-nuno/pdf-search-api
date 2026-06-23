using Ardalis.Result;
using FluentValidation;
using MediatR;

namespace Application.Common.Behaviors;

/// <summary>
/// Runs all FluentValidation validators for a request before the handler. On failure it
/// short-circuits with <c>Result&lt;T&gt;.Invalid(...)</c> (mapped to HTTP 400) rather than
/// throwing, so the same check covers every caller — HTTP and in-process alike.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

        if (failures.Count == 0)
        {
            return await next();
        }

        var errors = failures
            .Select(f => new ValidationError
            {
                Identifier = f.PropertyName,
                ErrorMessage = f.ErrorMessage,
                Severity = ValidationSeverity.Error
            })
            .ToList();

        if (TryBuildInvalidResult(errors, out TResponse? response))
        {
            return response!;
        }

        throw new ValidationException(failures);
    }

    private static bool TryBuildInvalidResult(List<ValidationError> errors, out TResponse? response)
    {
        Type responseType = typeof(TResponse);

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var method = responseType.GetMethod(nameof(Result.Invalid), [typeof(List<ValidationError>)]);
            response = (TResponse)method!.Invoke(null, [errors])!;
            return true;
        }

        if (responseType == typeof(Result))
        {
            response = (TResponse)(object)Result.Invalid(errors);
            return true;
        }

        response = default;
        return false;
    }
}
