using Domain.Documents;
using Raven.Client.Documents.Linq;

namespace Application.Common.Interfaces;

/// <summary>
/// The single, layer-owned persistence seam over the RavenDB async session. Handlers depend
/// on this — never on <c>IDocumentStore</c> or <c>IAsyncDocumentSession</c> directly. It is the
/// document-store equivalent of an EF <c>IApplicationDbContext</c>: the session is the unit of work.
/// </summary>
public interface IApplicationDbContext
{
    /// <summary>
    /// Queryable over <see cref="DocumentPage"/> backed by the <c>DocumentPages/Search</c>
    /// full-text index. Use <c>.Search(x =&gt; x.Content, term)</c> for full-text querying.
    /// </summary>
    IRavenQueryable<DocumentPage> DocumentPages { get; }

    /// <summary>Stages a document for insertion in the current unit of work.</summary>
    Task StoreAsync(DocumentPage page, CancellationToken ct = default);

    /// <summary>Marks a document for deletion in the current unit of work. Actual deletion is applied on <see cref="SaveChangesAsync"/>.</summary>
    void Delete(DocumentPage page);

    /// <summary>Commits all staged changes in the current unit of work.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the Lucene relevance score the index assigned to a tracked document, hiding
    /// the underlying <c>session.Advanced.GetMetadataFor(...)</c> call.
    /// </summary>
    double IndexScore<T>(T entity) where T : class;
}
