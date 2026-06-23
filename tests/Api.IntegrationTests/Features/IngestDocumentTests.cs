using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Api.IntegrationTests.Common;
using Application.Features.Ingestion.IngestDocument;
using Shouldly;
using Xunit;

namespace Api.IntegrationTests.Features;

public sealed class IngestDocumentTests(RavenTestFactory factory) : IClassFixture<RavenTestFactory>
{
    [Fact]
    public async Task Ingest_Should_Store_Only_NonEmpty_Pages()
    {
        HttpClient client = factory.CreateClient();
        byte[] pdf = TestPdf.Create("first page content", "   ", "third page content");

        var (status, body) = await client.PostPdfAsync<IngestDocumentResponse>("/v1/documents", pdf, "report.pdf");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldNotBeNull();
        body!.Success.ShouldBeTrue();
        body.Data.ShouldNotBeNull();
        body.Data!.FileName.ShouldBe("report.pdf");
        body.Data.PagesIngested.ShouldBe(2); // blank middle page is skipped
    }

    [Fact]
    public async Task Ingest_Should_Return_400_When_File_Is_Not_Pdf()
    {
        HttpClient client = factory.CreateClient();
        byte[] notPdf = Encoding.UTF8.GetBytes("just some text");

        var (status, body) = await client.PostPdfAsync<IngestDocumentResponse>("/v1/documents", notPdf, "notes.txt");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldNotBeNull();
        body!.Success.ShouldBeFalse();
        body.ValidationErrors.ShouldNotBeNull();
        body.ValidationErrors!.ShouldNotBeEmpty();
    }
}
