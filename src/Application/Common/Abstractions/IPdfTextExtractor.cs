namespace Application.Common.Abstractions;

/// <summary>Text extracted from a single PDF page.</summary>
public sealed record PdfPageText(int PageNumber, string Text);

/// <summary>
/// Abstracts per-page PDF text extraction so handlers stay unit-testable and never call the
/// PdfPig library statically. Implemented in Infrastructure.
/// </summary>
public interface IPdfTextExtractor
{
    /// <summary>
    /// Extracts text page-by-page. Pages whose text is empty or whitespace-only are omitted.
    /// </summary>
    IEnumerable<PdfPageText> Extract(Stream pdfStream);
}
