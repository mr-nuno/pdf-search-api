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
        HttpClient client = factory.CreateClient();

        // Unique token avoids collisions with other tests sharing the database.
        const string token = "zentilworp";
        byte[] pdf = TestPdf.Create($"intro page", $"the {token} appears on page two");
        var ingest = await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf, "manual.pdf");
        ingest.Status.ShouldBe(HttpStatusCode.Created);

        factory.WaitForIndexing();

        var (status, body) = await client.GetApiAsync<SearchResponseDto>($"/search?query={token}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotBeNull();
        body!.Success.ShouldBeTrue();
        body.Data.ShouldNotBeNull();
        body.Data!.TotalHits.ShouldBeGreaterThanOrEqualTo(1);

        SearchResultDto hit = body.Data.Results.ShouldHaveSingleItem();
        hit.SourceFileName.ShouldBe("manual.pdf");
        hit.PageNumber.ShouldBe(2);
        hit.Content.ShouldContain(token);
        hit.SearchScore.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Search_Should_Separate_Header_PageNumber_And_Markdown_Body()
    {
        HttpClient client = factory.CreateClient();

        const string bodyToken = "grimwackle";
        byte[] pdf = TestPdf.CreateStructuredPage(
            header: "CHAPTER 8 ADVENTURES",
            pageNumber: "108",
            heading: "Traps",
            $"The {bodyToken} lurks in the corridor.");
        var ingest = await client.PostPdfAsync<IngestDocumentResponse>("/documents", pdf, "book.pdf");
        ingest.Status.ShouldBe(HttpStatusCode.Created);

        factory.WaitForIndexing();

        var (status, body) = await client.GetApiAsync<SearchResponseDto>($"/search?query={bodyToken}");

        status.ShouldBe(HttpStatusCode.OK);
        SearchResultDto hit = body!.Data!.Results.ShouldHaveSingleItem();

        // Running header and page number are split into their own fields...
        hit.Header.ShouldBe("CHAPTER 8 ADVENTURES"); // the embedded "8" is kept, only "108" is taken
        hit.PageLabel.ShouldBe("108");

        // ...and the body is clean markdown: heading promoted, no header/page-number noise.
        hit.Content.ShouldContain(bodyToken);
        hit.Content.ShouldContain("## Traps");
        hit.Content.ShouldNotContain("CHAPTER");
        hit.Content.ShouldNotContain("108");
    }

    [Fact]
    public async Task Search_Should_Match_Text_In_The_Header()
    {
        HttpClient client = factory.CreateClient();

        const string headerToken = "snargleby";
        byte[] pdf = TestPdf.CreateStructuredPage(
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
        HttpClient client = factory.CreateClient();

        var (status, body) = await client.GetApiAsync<SearchResponseDto>("/search?query=");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldNotBeNull();
        body!.Success.ShouldBeFalse();
        body.ValidationErrors.ShouldNotBeNull();
        body.ValidationErrors!.ShouldContain(e => e.Property.Equals("query", StringComparison.OrdinalIgnoreCase));
    }
}
