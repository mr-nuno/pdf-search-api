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
    public void Keeps_Body_That_Runs_Into_The_Margin_Band_Out_Of_The_Header()
    {
        // The last lines sit inside the bottom margin band, but they are contiguous body text — not
        // a running footer isolated by whitespace.
        var pdf = TestPdf.CreateBodyRunningIntoBottomMargin(
            "The ranger descended into the dark",
            "crypt beneath the silent chapel",
            "where the ancient relic lay",
            "guarded by the grimwarden forever");

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Header.ShouldBeNull();
        page.Content.ShouldContain("grimwarden");
    }

    [Fact]
    public void Promotes_Bold_SameSize_Line_To_Markdown_Heading()
    {
        // The heading is bold but no larger than the body, so the size-ratio rule alone misses it.
        var pdf = TestPdf.CreateBoldHeadingPage(
            heading: "FRIA HANDLINGAR",
            "The serpent lurks in the corridor.");

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Content.ShouldContain("## FRIA HANDLINGAR");
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
    public void Reconstructs_Ruled_Table_As_Markdown()
    {
        var pdf = TestPdf.CreateRuledTablePage();

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Content.ShouldContain("| ITEM | PRICE | EFFECT |");
        page.Content.ShouldContain("| --- | --- | --- |");
        // Cells are assigned to the right column and two-word cells are joined in order.
        page.Content.ShouldContain("| Sword | 10 | Sharp blade |");
        page.Content.ShouldContain("| Potion | 3 | Heals wounds |");
    }

    [Fact]
    public void Reconstructs_Shaded_Row_Table_As_Markdown_Without_Rules()
    {
        // The rows are marked only by alternating shaded backgrounds — no ruling lines — so this
        // exercises the shaded-row detection path, including the unshaded trailing row that has no
        // rule beneath it.
        var pdf = TestPdf.CreateZebraTablePage();

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Content.ShouldContain("| T20 | RESULT |");
        page.Content.ShouldContain("| --- | --- |");
        page.Content.ShouldContain("| 1 | Alpha one |");
        // The unshaded gap row between two shaded rows is recovered...
        page.Content.ShouldContain("| 2 | Beta two |");
        // ...and the trailing unshaded last row (no rule below it) is captured too.
        page.Content.ShouldContain("| 4 | Delta four |");
    }

    [Fact]
    public void Splits_SideBySide_Tables_Into_Separate_Tables()
    {
        // Three zebra tables sit side by side with vertically-aligned rows. They must be
        // reconstructed as three independent two-column tables, not fused into one wide table.
        var pdf = TestPdf.CreateSideBySideZebraTablesPage();

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Content.ShouldContain("| T6 | LEFT |");
        page.Content.ShouldContain("| T6 | MID |");
        page.Content.ShouldContain("| T6 | RIGHT |");
        page.Content.ShouldContain("| 1 | left a |");
        page.Content.ShouldContain("| 5 | mid b |");
        page.Content.ShouldContain("| 9 | right c |");

        // The tables are never fused: no row carries cells from two tables, and each table keeps two
        // columns (its separator row is exactly two columns wide).
        page.Content.ShouldNotContain("| left a | 4 |");
        page.Content.ShouldNotContain("| --- | --- | --- |");
    }

    [Fact]
    public void Does_Not_Turn_Shaded_Prose_Into_A_Table()
    {
        // A highlighted callout: prose on shaded bands, but with no second column. It must stay
        // prose — shaded backgrounds alone never fabricate a table.
        var pdf = TestPdf.CreateShadedCalloutPage(
            "The ancient prophecy warns of a coming darkness",
            "that will sweep across the northern realms",
            "unless the chosen hero claims the relic first");

        var page = Extract(pdf).ShouldHaveSingleItem();

        page.Content.ShouldNotContain("|");
        page.Content.ShouldContain("ancient prophecy");
        page.Content.ShouldContain("northern realms");
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
