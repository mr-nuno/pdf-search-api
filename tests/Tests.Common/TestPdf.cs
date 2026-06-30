using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Tests.Common;

/// <summary>Builds small synthetic PDFs (one text line per page) for tests.</summary>
public static class TestPdf
{
    public static byte[] Create(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(PageSize.A4);
            if (!string.IsNullOrWhiteSpace(text))
            {
                page.AddText(text, 12, new PdfPoint(25, 700), font);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF with a running header and page number in the top margin, an enlarged
    /// heading, and body paragraphs — laid out like a real book page so layout-aware extraction can
    /// be exercised (header/page-number split, markdown heading, paragraph breaks).
    /// </summary>
    public static byte[] CreateStructuredPage(
        string header,
        string pageNumber,
        string heading,
        params string[] bodyParagraphs)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        // Top margin band (>90% of height): running header on the left, page number on the right.
        page.AddText(header, 12, new PdfPoint(25, 800), font);
        page.AddText(pageNumber, 12, new PdfPoint(560, 800), font);

        // Body region: an enlarged heading followed by paragraphs spaced far enough apart to read
        // as separate blocks.
        page.AddText(heading, 18, new PdfPoint(25, 740), font);

        double y = 705;
        foreach (var paragraph in bodyParagraphs)
        {
            page.AddText(paragraph, 12, new PdfPoint(25, y), font);
            y -= 40;
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF whose top margin band holds the printed page number in the LEFT
    /// corner followed by the running header to its right — the mirror of
    /// <see cref="CreateStructuredPage"/> — so the left-corner page-number strip can be exercised.
    /// </summary>
    public static byte[] CreateLeftPageNumberPage(
        string pageNumber,
        string header,
        params string[] bodyParagraphs)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        // Top margin band (>90% of height): page number in the left corner, header to its right.
        page.AddText(pageNumber, 12, new PdfPoint(25, 800), font);
        page.AddText(header, 12, new PdfPoint(120, 800), font);

        double y = 705;
        foreach (var paragraph in bodyParagraphs)
        {
            page.AddText(paragraph, 12, new PdfPoint(25, y), font);
            y -= 40;
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF whose body text runs all the way down into the bottom margin band
    /// (contiguous lines, no footer), so the lowest line sits inside the band but only a normal
    /// line-gap from the line above it. Used to check that body spilling into the margin is kept as
    /// body rather than mistaken for an isolated running footer.
    /// </summary>
    public static byte[] CreateBodyRunningIntoBottomMargin(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt; bottom 10% band is below ~84pt

        double y = 110; // start low so the final lines fall inside the bottom margin band
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(25, y), font);
            y -= 13;
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF whose heading is set in <b>bold at the same point size</b> as the
    /// body (rather than enlarged) — the way many books mark section titles. Exercises promoting a
    /// heading by font weight, not just by a larger size.
    /// </summary>
    public static byte[] CreateBoldHeadingPage(string heading, params string[] bodyParagraphs)
    {
        var builder = new PdfDocumentBuilder();
        var body = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        // Heading in bold, same 12pt as the body, set apart by a wide gap so it reads as its own line.
        page.AddText(heading, 12, new PdfPoint(25, 740), bold);

        double y = 700;
        foreach (var paragraph in bodyParagraphs)
        {
            page.AddText(paragraph, 12, new PdfPoint(25, y), body);
            y -= 40;
        }

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF whose running head sits in the BOTTOM margin (a running footer) with
    /// the printed page number on its own line below it — the layout of many books, and the mirror
    /// of <see cref="CreateStructuredPage"/>'s top header. Exercises detecting running furniture at
    /// the bottom of the page and stripping a centred/standalone page number.
    /// </summary>
    public static byte[] CreateBottomFooterPage(
        string footer,
        string pageNumber,
        params string[] bodyParagraphs)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        // Body region, well clear of the bottom margin band (<10% of the height).
        double y = 740;
        foreach (var paragraph in bodyParagraphs)
        {
            page.AddText(paragraph, 12, new PdfPoint(25, y), font);
            y -= 40;
        }

        // Bottom margin band: the running footer, with the page number on its own line beneath it.
        page.AddText(footer, 12, new PdfPoint(25, 45), font);
        page.AddText(pageNumber, 12, new PdfPoint(25, 25), font);

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF with two text columns separated by a wide vertical gutter — laid out
    /// like a two-column book page. The left and right columns share the same vertical bands (each
    /// left line sits beside a right line), so it exercises column-aware reconstruction: the columns
    /// must read as two separate blocks (left then right) rather than fusing each row's left and
    /// right text into one scrambled line. Lines within a column are placed close together so they
    /// join into one paragraph.
    /// </summary>
    public static byte[] CreateTwoColumnPage(string[] leftColumn, string[] rightColumn)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        const double leftX = 50;   // left column body ends well before the gutter
        const double rightX = 320; // right column starts well after the gutter
        const double startY = 740;
        const double leading = 13; // tight enough that a column's lines join into one paragraph

        AddColumn(page, font, leftColumn, leftX, startY, leading);
        AddColumn(page, font, rightColumn, rightX, startY, leading);

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF containing a ruled table: a header row of labels above the top rule,
    /// then data rows separated by drawn horizontal lines, with three columns aligned by x-position
    /// (the last column holding two-word cells). Exercises ruling-line table detection and markdown
    /// reconstruction.
    /// </summary>
    public static byte[] CreateRuledTablePage()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4);

        // Row-separating horizontal rules (wide and thin) spanning the full table width.
        foreach (var y in new[] { 735.0, 715.0, 695.0, 675.0 })
        {
            page.DrawLine(new PdfPoint(50, y), new PdfPoint(360, y), 1);
        }

        const double itemX = 60, priceX = 160, effectX = 250;

        // Header labels sit just above the top rule and define the columns.
        page.AddText("ITEM", 12, new PdfPoint(itemX, 740), font);
        page.AddText("PRICE", 12, new PdfPoint(priceX, 740), font);
        page.AddText("EFFECT", 12, new PdfPoint(effectX, 740), font);

        // The effect cell is a natural two-word phrase (normal word-spacing, one cell).
        void Row(double y, string item, string price, string effect)
        {
            page.AddText(item, 12, new PdfPoint(itemX, y), font);
            page.AddText(price, 12, new PdfPoint(priceX, y), font);
            page.AddText(effect, 12, new PdfPoint(effectX, y), font);
        }

        Row(722, "Sword", "10", "Sharp blade");
        Row(702, "Shield", "5", "Blocks blows");
        Row(682, "Potion", "3", "Heals wounds");

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF with a zebra-striped two-column table: alternating rows carry a
    /// light filled background and there are NO ruling lines, so detection must come from the shaded
    /// row backgrounds. The first and third rows are shaded, the second sits in the unshaded gap
    /// between them, and the fourth is left unshaded with no rule beneath it — exercising trailing-row
    /// capture. Mirrors the d20 "roll table" layout of the real source PDFs.
    /// </summary>
    public static byte[] CreateZebraTablePage()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        DrawZebraTable(
            page,
            font,
            left: 54,
            numberX: 62,
            textX: 100,
            width: 250,
            header: ("T20", "RESULT"),
            rows:
            [
                ("1", "Alpha one"),
                ("2", "Beta two"),
                ("3", "Gamma three"),
                ("4", "Delta four"),
            ]);

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF with three zebra-striped two-column tables sitting side by side,
    /// their rows vertically aligned (so their row boundaries interleave when read by vertical
    /// position). Exercises splitting horizontally-separated tables into independent regions rather
    /// than fusing them into one scrambled wide table — the layout of the real appearance/quirk
    /// roll-table pages.
    /// </summary>
    public static byte[] CreateSideBySideZebraTablesPage()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        DrawZebraTable(page, font, left: 40, numberX: 46, textX: 70, width: 150,
            header: ("T6", "LEFT"),
            rows: [("1", "left a"), ("2", "left b"), ("3", "left c")]);
        DrawZebraTable(page, font, left: 220, numberX: 226, textX: 250, width: 150,
            header: ("T6", "MID"),
            rows: [("4", "mid a"), ("5", "mid b"), ("6", "mid c")]);
        DrawZebraTable(page, font, left: 400, numberX: 406, textX: 430, width: 150,
            header: ("T6", "RIGHT"),
            rows: [("7", "right a"), ("8", "right b"), ("9", "right c")]);

        return builder.Build();
    }

    /// <summary>
    /// Builds a single-page PDF with several stacked shaded bands behind ordinary single-column
    /// prose — a highlighted callout, not a table (its rows have no second column). Used to check
    /// that shaded backgrounds alone never turn prose into a table: with no column structure the
    /// region is rejected and the text stays prose.
    /// </summary>
    public static byte[] CreateShadedCalloutPage(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        double y = 700;
        for (var i = 0; i < lines.Length; i++)
        {
            if (i % 2 == 0)
            {
                ShadeRow(page, left: 54, bottom: y - 5, width: 480, height: 18);
            }

            page.AddText(lines[i], 12, new PdfPoint(60, y), font);
            y -= 22;
        }

        return builder.Build();
    }

    /// <summary>Draws one zebra-striped two-column table: a header row of two labels, then data rows
    /// at a fixed pitch with the even-indexed rows given a light filled background and no rules.</summary>
    private static void DrawZebraTable(
        PdfPageBuilder page,
        PdfDocumentBuilder.AddedFont font,
        double left,
        double numberX,
        double textX,
        double width,
        (string Number, string Text) header,
        (string Number, string Text)[] rows)
    {
        const double headerY = 740, firstRowY = 712, rowPitch = 22;

        // Shaded backgrounds first: SetTextAndFillColor also tints text, so all fills are drawn and
        // the colour reset before any text is placed (text stays black).
        for (var i = 0; i < rows.Length; i += 2)
        {
            ShadeRow(page, left, bottom: firstRowY - i * rowPitch - 5, width, height: 18);
        }

        page.AddText(header.Number, 12, new PdfPoint(numberX, headerY), font);
        page.AddText(header.Text, 12, new PdfPoint(textX, headerY), font);

        for (var i = 0; i < rows.Length; i++)
        {
            var y = firstRowY - i * rowPitch;
            page.AddText(rows[i].Number, 12, new PdfPoint(numberX, y), font);
            page.AddText(rows[i].Text, 12, new PdfPoint(textX, y), font);
        }
    }

    /// <summary>Fills one light-tinted row background (no text), then resets the colour.</summary>
    private static void ShadeRow(PdfPageBuilder page, double left, double bottom, double width, double height)
    {
        page.SetTextAndFillColor(235, 222, 219);
        page.DrawRectangle(new PdfPoint(left, bottom), width, height, 1, fill: true);
        page.ResetColor();
    }

    private static void AddColumn(
        PdfPageBuilder page,
        PdfDocumentBuilder.AddedFont font,
        IReadOnlyList<string> lines,
        double x,
        double startY,
        double leading)
    {
        var y = startY;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(x, y), font);
            y -= leading;
        }
    }

    /// <summary>
    /// Builds a single-page PDF whose body is a run of consecutive lines placed close together
    /// (small leading) so they cluster into one paragraph — used to exercise wrapped-line joining
    /// and de-hyphenation.
    /// </summary>
    public static byte[] CreateBodyLines(double leading, params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        double y = 700;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(25, y), font);
            y -= leading;
        }

        return builder.Build();
    }
}
