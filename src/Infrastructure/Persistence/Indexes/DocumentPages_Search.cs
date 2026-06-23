using Domain.Documents;
using Raven.Client.Documents.Indexes;

namespace Infrastructure.Persistence.Indexes;

/// <summary>
/// Full-text index over <see cref="DocumentPage"/> content. The class name maps to the
/// RavenDB index name <c>DocumentPages/Search</c>.
/// </summary>
public sealed class DocumentPages_Search : AbstractIndexCreationTask<DocumentPage>
{
    public DocumentPages_Search()
    {
        Map = pages => from page in pages
                       select new
                       {
                           page.Content,
                           page.Header,
                           page.SourceFileName,
                           page.Tag
                       };

        // Analyze Content and Header with Lucene's standard analyzer for stemming/casing-insensitive
        // matching. Header is indexed so chapter-title text (split out of Content) stays searchable.
        Index(x => x.Content, FieldIndexing.Search);
        Analyze(x => x.Content, "StandardAnalyzer");
        Index(x => x.Header, FieldIndexing.Search);
        Analyze(x => x.Header, "StandardAnalyzer");

        // Use the Lucene engine so relevance scores are exposed via document metadata
        // (@index-score). The default Corax engine does not populate it.
        SearchEngineType = Raven.Client.Documents.Indexes.SearchEngineType.Lucene;
    }
}
