using Application.Common.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Infrastructure.Pdf;

/// <summary>Extracts per-page text from a PDF using UglyToad.PdfPig.</summary>
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    public IEnumerable<PdfPageText> Extract(Stream pdfStream)
    {
        using PdfDocument document = PdfDocument.Open(pdfStream);

        foreach (Page page in document.GetPages())
        {
            string text = page.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            yield return new PdfPageText(page.Number, text);
        }
    }
}
