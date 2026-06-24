using Application.Common.Interfaces;
using Infrastructure.Pdf;
using Shouldly;
using Tests.Common;

namespace Infrastructure.UnitTests;

/// <summary>
/// Direct unit tests for <see cref="PdfPigTextExtractor"/> — no web host or RavenDB, just real
/// PDFs built by <see cref="TestPdf"/> fed straight through the extractor. These lock the
/// layout-aware behaviors (header/page-number split, heading promotion, de-hyphenation, blank-page
/// omission) that the integration tests only cover end-to-end.
/// </summary>
public class PdfPigTextExtractorTests
{
    private static List<PdfPageText> Extract(byte[] pdf)
    {
        var extractor = new PdfPigTextExtractor();
        using var stream = new MemoryStream(pdf);
        return extractor.Extract(stream).ToList();
    }

    [Fact]
    public void Strips_PageNumber_From_End_Of_Header_Keeping_Embedded_Numeral()
    {
        var pdf = TestPdf.CreateStructuredPage(
            header: "CHAPTER 8 ADVENTURES",
            pageNumber: "108",
            heading: "Traps",
            "The serpent lurks in the corridor.");

        var page = Extract(pdf).ShouldHaveSingleItem();

        // Trailing corner numeral "108" is stripped; the embedded "8" is kept.
        page.Header.ShouldBe("CHAPTER 8 ADVENTURES");
    }

    [Fact]
    public void Strips_PageNumber_From_Start_Of_Header_Keeping_Embedded_Numeral()
    {
        var pdf = TestPdf.CreateLeftPageNumberPage(
            pageNumber: "108",
            header: "CHAPTER 8 ADVENTURES",
            "The serpent lurks in the corridor.");

        var page = Extract(pdf).ShouldHaveSingleItem();

        // Leading corner numeral "108" is stripped; the embedded "8" is kept.
        page.Header.ShouldBe("CHAPTER 8 ADVENTURES");
    }

    [Fact]
    public void Promotes_Enlarged_Line_To_Markdown_Heading()
    {
        var pdf = TestPdf.CreateStructuredPage(
            header: "CHAPTER 1",
            pageNumber: "1",
            heading: "Traps",
            "The serpent lurks in the corridor.");

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Content.ShouldContain("## Traps");
    }

    [Fact]
    public void Joins_Wrapped_Lines_And_DeHyphenates()
    {
        var pdf = TestPdf.CreateBodyLines(
            leading: 13,
            "The treasure lies beyond the riv-",
            "erbank near the old mill.");

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Content.ShouldContain("riverbank");
        page.Content.ShouldNotContain("riv-");
        page.Content.ShouldNotContain("riv- ");
    }

    [Fact]
    public void Omits_Blank_Pages_And_Keeps_Physical_PageNumbers()
    {
        var pdf = TestPdf.Create("first page content", "   ", "third page content");

        var pages = Extract(pdf);

        pages.Count.ShouldBe(2);
        pages.Select(p => p.PageNumber).ShouldBe(new[] { 1, 3 });
    }
}
