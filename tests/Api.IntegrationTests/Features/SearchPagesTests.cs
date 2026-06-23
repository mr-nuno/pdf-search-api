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
