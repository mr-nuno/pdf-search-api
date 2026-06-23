using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Api.IntegrationTests.Common;

/// <summary>Builds small real PDFs (one text line per page) for ingestion tests.</summary>
public static class TestPdf
{
    public static byte[] Create(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (string text in pageTexts)
        {
            PdfPageBuilder page = builder.AddPage(PageSize.A4);
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
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);

        PdfPageBuilder page = builder.AddPage(PageSize.A4); // 595 x 842 pt

        // Top margin band (>90% of height): running header on the left, page number on the right.
        page.AddText(header, 12, new PdfPoint(25, 800), font);
        page.AddText(pageNumber, 12, new PdfPoint(560, 800), font);

        // Body region: an enlarged heading followed by paragraphs spaced far enough apart to read
        // as separate blocks.
        page.AddText(heading, 18, new PdfPoint(25, 740), font);

        double y = 705;
        foreach (string paragraph in bodyParagraphs)
        {
            page.AddText(paragraph, 12, new PdfPoint(25, y), font);
            y -= 40;
        }

        return builder.Build();
    }
}
