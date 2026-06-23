using System.Collections.Generic;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.TestDriver;

namespace Api.IntegrationTests.Common;

/// <summary>
/// Boots the API against a real RavenDB test-mode server (RavenDB.TestDriver), substituting the
/// production document store for a fresh per-factory database. The same <see cref="RavenConventions"/>
/// is applied to the test store and indexes are created by the app's hosted service.
/// </summary>
public sealed class RavenTestFactory : WebApplicationFactory<Program>
{
    private readonly TestRavenDriver _driver = new();

    public IDocumentStore Store { get; }

    public RavenTestFactory() => Store = _driver.CreateStore();

    public void WaitForIndexing() => _driver.WaitForIndexingPublic(Store);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RavenDb:Urls:0"] = Store.Urls[0],
                ["RavenDb:Database"] = Store.Database
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDocumentStore>();
            services.RemoveAll<IAsyncDocumentSession>();

            services.AddSingleton(Store);
            services.AddScoped(sp => sp.GetRequiredService<IDocumentStore>().OpenAsyncSession());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _driver.Dispose();
        }
    }

    private sealed class TestRavenDriver : RavenTestDriver
    {
        static TestRavenDriver()
        {
            var options = new Raven.TestDriver.TestServerOptions();
            // Embedded test server: skip the strict licensing requirement (dev/CI only).
            options.Licensing.ThrowOnInvalidOrMissingLicense = false;
            ConfigureServer(options);
        }

        public IDocumentStore CreateStore() => GetDocumentStore();

        public void WaitForIndexingPublic(IDocumentStore store) => WaitForIndexing(store);

        protected override void PreInitialize(IDocumentStore documentStore) =>
            RavenConventions.Apply(documentStore);
    }
}
