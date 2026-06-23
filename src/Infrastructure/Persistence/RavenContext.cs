using Application.Common.Interfaces;
using Domain.Documents;
using Raven.Client;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using Infrastructure.Persistence.Indexes;

namespace Infrastructure.Persistence;

/// <summary>
/// Implements the application persistence seam over a scoped RavenDB async session.
/// One session = one unit of work per request.
/// </summary>
public sealed class RavenContext(IAsyncDocumentSession session) : IApplicationDbContext
{
    public IRavenQueryable<DocumentPage> DocumentPages =>
        session.Query<DocumentPage, DocumentPages_Search>();

    public Task StoreAsync(DocumentPage page, CancellationToken ct = default) =>
        session.StoreAsync(page, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        session.SaveChangesAsync(ct);

    public double IndexScore<T>(T entity) where T : class
    {
        var metadata = session.Advanced.GetMetadataFor(entity);
        return metadata.TryGetValue(Constants.Documents.Metadata.IndexScore, out object? value) && value is not null
            ? Convert.ToDouble(value)
            : 0d;
    }
}
