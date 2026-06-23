using Application.Common.Abstractions;
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
        RavenDbOptions options = configuration.GetSection(RavenDbOptions.SectionName).Get<RavenDbOptions>()
            ?? throw new InvalidOperationException($"Missing '{RavenDbOptions.SectionName}' configuration section.");

        IDocumentStore store = new DocumentStore
        {
            Urls = options.Urls,
            Database = options.Database
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
}
