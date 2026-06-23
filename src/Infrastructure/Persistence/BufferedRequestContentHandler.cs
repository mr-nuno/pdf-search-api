using System.Net.Http.Headers;

namespace Infrastructure.Persistence;

/// <summary>
/// Works around RavenDB client requests failing with
/// <c>InvalidOperationException: "Already called previously, or called after EnsureCompletedAsync"</c>
/// when the store talks to a TLS endpoint (e.g. the mkcert-fronted <c>ravendb.pew.local</c> or
/// RavenDB Cloud). There, <see cref="System.Net.Http.SocketsHttpHandler"/> transparently retries a
/// request on a fresh connection after the first connection is closed — and re-invokes the request
/// body's <c>SerializeToStreamAsync</c>. RavenDB's <c>BlittableJsonContent</c> is single-use and
/// throws on that second call.
///
/// Buffering the body into a re-readable <see cref="ByteArrayContent"/> before it reaches the socket
/// handler makes the retry safe. Only materialized JSON content is buffered; the open-ended streaming
/// content used by bulk insert (<c>BulkInsertStreamExposerContent</c>) is left untouched so it can
/// keep streaming.
/// </summary>
internal sealed class BufferedRequestContentHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is { } content && content.GetType().Name == "BlittableJsonContent")
        {
            var bytes = await content.ReadAsByteArrayAsync(ct);
            var buffered = new ByteArrayContent(bytes);
            CopyHeaders(content.Headers, buffered.Headers);
            request.Content = buffered;
        }

        return await base.SendAsync(request, ct);
    }

    private static void CopyHeaders(HttpContentHeaders from, HttpContentHeaders to)
    {
        foreach (var header in from)
            to.TryAddWithoutValidation(header.Key, header.Value);
    }
}
