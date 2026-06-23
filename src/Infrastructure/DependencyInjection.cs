using System.Security.Cryptography.X509Certificates;
using Application.Common.Interfaces;
using Infrastructure.Persistence;
using Infrastructure.Pdf;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raven.Client.Documents;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(RavenDbOptions.SectionName).Get<RavenDbOptions>()
                      ?? throw new InvalidOperationException($"Missing '{RavenDbOptions.SectionName}' configuration section.");

        IDocumentStore store = new DocumentStore
        {
            Urls = options.Urls,
            Database = options.Database,
            Certificate = LoadCertificate(options)
        };
        RavenConventions.Apply(store);
        store.Initialize();

        // The store is a singleton seen only by Infrastructure (sessions, index creation).
        services.AddSingleton(store);

        // One session = one unit of work per request.
        services.AddScoped(sp => sp.GetRequiredService<IDocumentStore>().OpenAsyncSession());
        services.AddScoped<IApplicationDbContext, RavenContext>();

        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddHostedService<IndexInitializerHostedService>();

        return services;
    }

    // A secured RavenDB cluster (e.g. RavenDB Cloud over https) authenticates the client with an
    // X.509 client certificate; an unsecured store — including the TLS-fronted but open
    // ravendb.pew.local — needs none. Returning null leaves the store unauthenticated.
    private static X509Certificate2? LoadCertificate(RavenDbOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath))
            return null;

        if (!File.Exists(options.CertificatePath))
            throw new FileNotFoundException(
                $"RavenDB client certificate not found at '{options.CertificatePath}'.", options.CertificatePath);

        return X509CertificateLoader.LoadPkcs12FromFile(
            options.CertificatePath,
            string.IsNullOrEmpty(options.CertificatePassword) ? null : options.CertificatePassword);
    }
}
