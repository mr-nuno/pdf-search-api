namespace Application.Common.Interfaces;

/// <summary>Abstracts the system clock. Never call <c>DateTimeOffset.UtcNow</c> directly.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
