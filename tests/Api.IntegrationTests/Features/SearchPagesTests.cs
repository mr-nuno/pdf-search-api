using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Api.IntegrationTests.Common;
using Application.Features.Ingestion.IngestDocument;
using Application.Features.Search.SearchPages;
using Shouldly;
using Xunit;

namespace Api.IntegrationTests.Features;

public sealed class SearchPagesTests(RavenTestFactory factory) : IClassFixture<RavenTestFactory>
{
    [Fact]
    public async Task Search_Should_Return_Matching_Page_With_Metadata_And_Score()
    {
        var client = factory.CreateClient();

        // Unique token avoids collisions with other tests sharing the database.
        const string token = "zentilworp";
        var pdf = TestPdf.Create($"intro page", $"the {token} appears on page two");
        var ingest = await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf, "manual.pdf");
        ingest.Status.ShouldBe(HttpStatusCode.Created);

        factory.WaitForIndexing();

        var (status, body) = await client.GetApiAsync<SearchResponseDto>($"/search?query={token}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body!.Success.ShouldBeTrue();
        body.Data.ShouldNotBeNull();
        body.Data!.TotalHits.ShouldBeGreaterThanOrEqualTo(1);

        var hit = body.Data.Results.ShouldHaveSingleItem();
        hit.SourceFileName.ShouldBe("manual.pdf");
        hit.PhysicalPageNumber.ShouldBe(2);
        hit.Content.ShouldContain(token);
        hit.SearchScore.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Search_Should_Separate_Header_PageNumber_And_Markdown_Body()
    {
        var client = factory.CreateClient();

        const string bodyToken = "grimwackle";
        var pdf = TestPdf.CreateStructuredPage(
            header: "CHAPTER 8 ADVENTURES",
            pageNumber: "108",
            heading: "Traps",
            $"The {bodyToken} lurks in the corridor.");
        var ingest = await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf, "book.pdf");
        ingest.Status.ShouldBe(HttpStatusCode.Created);

        factory.WaitForIndexing();

        var (status, body) = await client.GetApiAsync<SearchResponseDto>($"/search?query={bodyToken}");

        status.ShouldBe(HttpStatusCode.OK);
        var hit = body!.Data!.Results.ShouldHaveSingleItem();

        // Running header is split into its own field; the embedded "8" is kept, only "108" is stripped.
        hit.Header.ShouldBe("CHAPTER 8 ADVENTURES");

        // Body is clean markdown: heading promoted, no header/page-number noise.
        hit.Content.ShouldContain(bodyToken);
        hit.Content.ShouldNotContain("CHAPTER");
        hit.Content.ShouldNotContain("108");
    }

    [Fact]
    public async Task Search_Should_Match_Text_In_The_Header()
    {
        var client = factory.CreateClient();

        const string headerToken = "snargleby";
        var pdf = TestPdf.CreateStructuredPage(
            header: $"CHAPTER {headerToken}",
            pageNumber: "12",
            heading: "Intro",
            "An unrelated body sentence.");
        var ingest = await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf, "headers.pdf");
        ingest.Status.ShouldBe(HttpStatusCode.Created);

        factory.WaitForIndexing();

        var (status, body) = await client.GetApiAsync<SearchResponseDto>($"/search?query={headerToken}");

        status.ShouldBe(HttpStatusCode.OK);
        body!.Data!.TotalHits.ShouldBeGreaterThanOrEqualTo(1);
        body.Data.Results.ShouldContain(r =>
            r.SourceFileName == "headers.pdf" && r.Header != null && r.Header.Contains(headerToken, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_Should_Return_400_When_Query_Is_Empty()
    {
        var client = factory.CreateClient();

        var (status, body) = await client.GetApiAsync<SearchResponseDto>("/search?query=");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldNotBeNull();
        body!.Success.ShouldBeFalse();
        body.ValidationErrors.ShouldNotBeNull();
        body.ValidationErrors!.ShouldContain(e => e.Property.Equals("query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_Should_Filter_By_Tag_When_Tag_Is_Provided()
    {
        var client = factory.CreateClient();

        const string token = "taggable";
        var pdf1 = TestPdf.Create("page", $"this contains {token} but with hr tag");
        var pdf2 = TestPdf.Create("page", $"this contains {token} but with finance tag");

        var ingest1 = await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf1, "hr.pdf", tag: "hr");
        ingest1.Status.ShouldBe(HttpStatusCode.Created);

        var ingest2 = await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf2, "finance.pdf", tag: "finance");
        ingest2.Status.ShouldBe(HttpStatusCode.Created);

        factory.WaitForIndexing();

        // Search with finance tag
        var (status, body) = await client.GetApiAsync<SearchResponseDto>($"/search?query={token}&tag=finance");

        status.ShouldBe(HttpStatusCode.OK);
        body!.Data!.TotalHits.ShouldBe(1);
        var hit = body.Data.Results.ShouldHaveSingleItem();
        hit.SourceFileName.ShouldBe("finance.pdf");
        hit.Tag.ShouldBe("finance");

        // Search across all tags (tag omitted)
        var (statusAll, bodyAll) = await client.GetApiAsync<SearchResponseDto>($"/search?query={token}");
        statusAll.ShouldBe(HttpStatusCode.OK);
        bodyAll!.Data!.TotalHits.ShouldBeGreaterThanOrEqualTo(2);
        bodyAll.Data.Results.ShouldContain(r => r.SourceFileName == "hr.pdf");
        bodyAll.Data.Results.ShouldContain(r => r.SourceFileName == "finance.pdf");
    }
}
