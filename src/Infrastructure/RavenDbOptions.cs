namespace Infrastructure;

/// <summary>Binds the <c>RavenDb</c> configuration section.</summary>
public sealed class RavenDbOptions
{
    public const string SectionName = "RavenDb";

    public string[] Urls { get; set; } = [];

    public string Database { get; set; } = string.Empty;
}
