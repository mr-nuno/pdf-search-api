namespace Application.Common.Interfaces;

/// <summary>Layout-aware text extracted from a single PDF page.</summary>
/// <param name="PageNumber">The 1-based physical page index within the source PDF.</param>
/// <param name="Content">The page body, formatted as markdown (paragraphs separated by blank
/// lines, large lines promoted to headings). Excludes running headers/footers and page numbers.</param>
/// <param name="Header">The running-header text for the page, or <c>null</c> if none was detected.</param>
public sealed record PdfPageText(int PageNumber, string Content, string? Header);

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
