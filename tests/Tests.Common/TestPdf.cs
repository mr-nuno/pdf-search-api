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
