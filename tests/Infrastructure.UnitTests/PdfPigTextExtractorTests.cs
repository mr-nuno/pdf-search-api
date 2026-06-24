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
    public void Captures_Bottom_Running_Footer_As_Header_And_Strips_PageNumber()
    {
        var pdf = TestPdf.CreateBottomFooterPage(
            footer: "KAPITEL 4 – STRID & SKADA",
            pageNumber: "108",
            "The serpent lurks in the corridor.");

        var page = Extract(pdf).ShouldHaveSingleItem();

        // The running head sits in the bottom margin, yet it is still the page's header.
        page.Header.ShouldBe("KAPITEL 4 – STRID & SKADA");

        // The body keeps its prose; the footer and the standalone page number are stripped out.
        page.Content.ShouldContain("serpent");
        page.Content.ShouldNotContain("KAPITEL");
        page.Content.ShouldNotContain("108");
    }

    [Fact]
    public void Reads_Two_Columns_In_Order_Without_Fusing_Rows()
    {
        // Each left line sits beside a right line on the same vertical band. A layout-unaware
        // reconstruction would glue them into one row ("...ranger Meanwhile the cloaked...").
        var pdf = TestPdf.CreateTwoColumnPage(
            leftColumn:
            [
                "Across the moor the ranger",
                "tracked a wounded alpha",
                "bravo through cold mist",
            ],
            rightColumn:
            [
                "Meanwhile the cloaked",
                "RIGHTGUILD plotted in",
                "the ruined cathedral",
            ]);

        var page = Extract(pdf).ShouldHaveSingleItem();

        // The left column reconstructs as one contiguous, joined block...
        page.Content.ShouldContain("wounded alpha bravo through");
        // ...and the right column is present as its own block, read after the left one.
        page.Content.ShouldContain("RIGHTGUILD plotted in");
        page.Content.IndexOf("Across the moor", StringComparison.Ordinal)
            .ShouldBeLessThan(page.Content.IndexOf("Meanwhile the cloaked", StringComparison.Ordinal));

        // Columns are never fused row-by-row.
        page.Content.ShouldNotContain("ranger Meanwhile");
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
