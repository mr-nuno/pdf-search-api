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
}
