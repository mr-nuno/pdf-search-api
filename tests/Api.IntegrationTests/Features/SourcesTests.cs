using System.Net;
using System.Threading.Tasks;
using Api.IntegrationTests.Common;
using Application.Features.Ingestion.IngestDocument;
using Application.Features.Sources.DeleteSource;
using Application.Features.Sources.GetSources;
using Shouldly;
using Tests.Common;
using Xunit;

namespace Api.IntegrationTests.Features;

public sealed class SourcesTests(RavenTestFactory factory) : IClassFixture<RavenTestFactory>
{
    [Fact]
    public async Task GetSources_Returns_Ingested_Files_Grouped_By_FileName()
    {
        var client = factory.CreateClient();
        var pdf = TestPdf.Create("alpha content", "beta content");

        await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf, "sources-list-a.pdf", ["finance"]);
        await client.PostPdfAsync<IngestDocumentResponse>("/documents", TestPdf.Create("other"), "sources-list-b.pdf", ["hr"]);
        factory.WaitForIndexing();

        var (status, body) = await client.GetApiAsync<GetSourcesResponse>("/sources");

        status.ShouldBe(HttpStatusCode.OK);
        body!.Success.ShouldBeTrue();
        body.Data!.Sources.ShouldContain(s =>
            s.SourceFileName == "sources-list-a.pdf" && s.PageCount == 2 && s.Tags.Contains("finance"));
        body.Data.Sources.ShouldContain(s =>
            s.SourceFileName == "sources-list-b.pdf" && s.PageCount == 1 && s.Tags.Contains("hr"));
    }

    [Fact]
    public async Task DeleteSource_Removes_All_Pages_And_Reports_Orphaned_Tags()
    {
        var client = factory.CreateClient();
        var fileName = "sources-delete.pdf";

        await client.PostPdfAsync<IngestDocumentResponse>("/documents", TestPdf.Create("p1", "p2"), fileName, ["unique-delete-tag"]);
        factory.WaitForIndexing();

        var (status, body) = await client.DeleteApiAsync<DeleteSourceResponse>($"/sources?sourceFileName={fileName}");

        status.ShouldBe(HttpStatusCode.OK);
        body!.Success.ShouldBeTrue();
        body.Data!.SourceFileName.ShouldBe(fileName);
        body.Data.PagesDeleted.ShouldBe(2);
        body.Data.TagsRemoved.ShouldContain("unique-delete-tag");
    }

    [Fact]
    public async Task DeleteSource_Returns_404_When_File_Not_Found()
    {
        var client = factory.CreateClient();

        var (status, body) = await client.DeleteApiAsync<DeleteSourceResponse>("/sources?sourceFileName=nonexistent.pdf");

        status.ShouldBe(HttpStatusCode.NotFound);
        body!.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteSource_Does_Not_Report_Tag_As_Removed_When_Another_File_Shares_It()
    {
        var client = factory.CreateClient();
        var sharedTag = "sources-shared-tag";

        await client.PostPdfAsync<IngestDocumentResponse>("/documents", TestPdf.Create("doc x"), "sources-shared-x.pdf", [sharedTag]);
        await client.PostPdfAsync<IngestDocumentResponse>("/documents", TestPdf.Create("doc y"), "sources-shared-y.pdf", [sharedTag]);
        factory.WaitForIndexing();

        var (_, body) = await client.DeleteApiAsync<DeleteSourceResponse>($"/sources?sourceFileName=sources-shared-x.pdf");

        body!.Data!.TagsRemoved.ShouldNotContain(sharedTag);
    }

    [Fact]
    public async Task PutDocument_Replaces_Existing_Pages_For_Same_File()
    {
        var client = factory.CreateClient();
        var fileName = "sources-reingest.pdf";

        // Initial ingest: 3 pages
        await client.PostPdfAsync<IngestDocumentResponse>("/documents", TestPdf.Create("old1", "old2", "old3"), fileName);
        factory.WaitForIndexing();

        // Re-ingest via PUT: 2 pages
        var (status, body) = await client.PutPdfAsync<IngestDocumentResponse>("/documents", TestPdf.Create("new1", "new2"), fileName);

        status.ShouldBe(HttpStatusCode.OK);
        body!.Success.ShouldBeTrue();
        body.Data!.FileName.ShouldBe(fileName);
        body.Data.PagesIngested.ShouldBe(2);

        factory.WaitForIndexing();
        var (_, sourcesBody) = await client.GetApiAsync<GetSourcesResponse>("/sources");
        var source = sourcesBody!.Data!.Sources.First(s => s.SourceFileName == fileName);
        source.PageCount.ShouldBe(2);
    }

    [Fact]
    public async Task PutDocument_Works_When_File_Was_Not_Previously_Ingested()
    {
        var client = factory.CreateClient();
        var fileName = "sources-putonly.pdf";

        var (status, body) = await client.PutPdfAsync<IngestDocumentResponse>("/documents", TestPdf.Create("only page"), fileName);

        status.ShouldBe(HttpStatusCode.OK);
        body!.Success.ShouldBeTrue();
        body.Data!.PagesIngested.ShouldBe(1);
    }
}
