using Domain.Documents;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;

namespace Infrastructure.Persistence;

/// <summary>
/// The single place that shapes how documents are stored (collection naming, id separators).
/// Applied identically by the production store and the integration-test store so tests never
/// re-derive naming/id rules.
/// </summary>
public static class RavenConventions
{
    public static void Apply(IDocumentStore store)
    {
        store.Conventions.IdentityPartsSeparator = '/';
        store.Conventions.FindCollectionName = type =>
            type == typeof(DocumentPage)
                ? "DocumentPages"
                : DocumentConventions.DefaultGetCollectionName(type);
    }
}
