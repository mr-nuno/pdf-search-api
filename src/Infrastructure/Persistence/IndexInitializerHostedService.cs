using Infrastructure.Persistence.Indexes;
using Microsoft.Extensions.Hosting;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Exceptions;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;

namespace Infrastructure.Persistence;

/// <summary>
/// Ensures the configured database exists and creates the RavenDB indexes defined in this
/// assembly at application startup.
/// </summary>
public sealed class IndexInitializerHostedService(IDocumentStore store) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseExistsAsync(cancellationToken);
        await IndexCreation.CreateIndexesAsync(typeof(DocumentPages_Search).Assembly, store, token: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureDatabaseExistsAsync(CancellationToken ct)
    {
        var record =
            await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(store.Database), ct);

        if (record is not null)
        {
            return;
        }

        try
        {
            await store.Maintenance.Server.SendAsync(
                new CreateDatabaseOperation(new DatabaseRecord(store.Database)), ct);
        }
        catch (ConcurrencyException)
        {
            // Created concurrently by another instance — safe to ignore.
        }
    }
}
