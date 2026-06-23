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

        // Buffer materialized JSON request bodies so RavenDB's single-use BlittableJsonContent
        // survives the transparent connection-retry SocketsHttpHandler performs against a TLS
        // endpoint (mkcert-fronted ravendb.pew.local, RavenDB Cloud). Without this the retry
        // re-serializes the body and throws "Already called previously, or called after
        // EnsureCompletedAsync". See BufferedRequestContentHandler. The handler RavenDB hands us is
        // pre-configured (client certificate, etc.), so we only wrap it.
        store.Conventions.CreateHttpClient =
            handler => new HttpClient(new BufferedRequestContentHandler(handler));
    }
}
