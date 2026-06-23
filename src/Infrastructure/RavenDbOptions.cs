namespace Infrastructure;

/// <summary>Binds the <c>RavenDb</c> configuration section.</summary>
public sealed class RavenDbOptions
{
    public const string SectionName = "RavenDb";

    public string[] Urls { get; set; } = [];

    public string Database { get; set; } = string.Empty;

    /// <summary>Path to an X.509 client certificate (.pfx). Blank for an unsecured store.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Password for the client certificate; blank when the .pfx has none.</summary>
    public string? CertificatePassword { get; set; }
}
