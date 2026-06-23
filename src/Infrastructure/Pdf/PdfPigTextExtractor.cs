using System.Text;
using System.Text.RegularExpressions;
using Application.Common.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Infrastructure.Pdf;

/// <summary>
/// Extracts per-page text from a PDF using UglyToad.PdfPig. Rather than the layout-unaware
/// <see cref="Page.Text"/> property (which glues running headers, page numbers and body text into
/// one run), this reconstructs visual lines from word positions, splits off the running header and
/// printed page number, and renders the body as markdown so consumers can display it cleanly.
/// </summary>
public sealed partial class PdfPigTextExtractor : IPdfTextExtractor
{
    // Fraction of page height treated as the top/bottom margin bands where running headers,
    // footers and page numbers live. A line whose vertical centre is above the header threshold
    // (or below the footer threshold) is treated as margin furniture, not body.
    private const double HeaderBandTop = 0.90;
    private const double FooterBandBottom = 0.10;

    // A single body line whose font is at least this much larger than the page's body text is
    // promoted to a markdown heading.
    private const double HeadingSizeRatio = 1.3;

    [GeneratedRegex(@"^\d{1,4}$")]
    private static partial Regex NumericOnly();

    public IEnumerable<PdfPageText> Extract(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);

        foreach (var page in document.GetPages())
        {
            var extracted = ExtractPage(page);
            if (extracted is not null)
            {
                yield return extracted;
            }
        }
    }

    private static PdfPageText? ExtractPage(Page page)
    {
        List<Word> words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();
        if (words.Count == 0)
        {
            return null;
        }

        var lines = GroupIntoLines(words);

        var headerThreshold = page.Height * HeaderBandTop;
        var footerThreshold = page.Height * FooterBandBottom;

        var headerLines = new List<string>();
        var bodyLines = new List<Line>();
        string? pageLabel = null;

        foreach (var line in lines)
        {
            var inHeaderBand = line.CenterY >= headerThreshold;
            var inFooterBand = line.CenterY <= footerThreshold;

            if (inHeaderBand || inFooterBand)
            {
                // A numeric-only token in a margin band is the printed page number; whatever
                // header text remains stays as the running header (footer prose is dropped).
                var remainder = TakePageLabel(line, ref pageLabel);
                if (remainder.Length > 0 && inHeaderBand)
                {
                    headerLines.Add(remainder);
                }
            }
            else
            {
                bodyLines.Add(line);
            }
        }

        var header = headerLines.Count > 0 ? string.Join(" ", headerLines) : null;
        var content = BuildMarkdown(bodyLines);

        if (content.Length == 0 && header is null)
        {
            return null;
        }

        return new PdfPageText(page.Number, content, header, pageLabel);
    }

    /// <summary>Clusters words whose vertical centres are close into single visual lines,
    /// ordered top-to-bottom and, within each line, left-to-right.</summary>
    private static List<Line> GroupIntoLines(IReadOnlyList<Word> words)
    {
        var medianHeight = Median(words.Select(w => w.BoundingBox.Height));
        var tolerance = Math.Max(medianHeight * 0.5, 1.0);

        // Sort top-to-bottom (PDF Y grows upward, so larger centre = higher on the page) so that
        // words belonging to one line are contiguous and a new line starts on a vertical jump.
        var ordered = words.OrderByDescending(w => CenterY(w.BoundingBox)).ToList();

        var buckets = new List<List<Word>>();
        foreach (var word in ordered)
        {
            var y = CenterY(word.BoundingBox);
            if (buckets.Count == 0 || Math.Abs(CenterY(buckets[^1][0].BoundingBox) - y) > tolerance)
            {
                buckets.Add([word]);
            }
            else
            {
                buckets[^1].Add(word);
            }
        }

        var lines = new List<Line>(buckets.Count);
        foreach (var bucket in buckets)
        {
            var sorted = bucket.OrderBy(w => w.BoundingBox.Left).ToList();
            lines.Add(new Line(
                Text: string.Join(" ", sorted.Select(w => w.Text)),
                CenterY: sorted.Average(w => CenterY(w.BoundingBox)),
                Height: Median(sorted.Select(w => w.BoundingBox.Height)),
                FontSize: Median(sorted.SelectMany(w => w.Letters).Select(l => l.PointSize)),
                Words: sorted));
        }

        return lines;
    }

    /// <summary>Pulls a page number out of a margin line into <paramref name="pageLabel"/> and returns
    /// the remaining text. Only the line's extreme tokens (the corners, where page numbers sit) are
    /// considered, so a numeral embedded between header words (e.g. the "8" in "KAPITEL 8") is kept.</summary>
    private static string TakePageLabel(Line line, ref string? pageLabel)
    {
        var words = line.Words;

        // Only the first and last token of the line are page-number candidates (the corners).
        var labelIndex = -1;
        if (pageLabel is null)
        {
            if (NumericOnly().IsMatch(words[0].Text.Trim()))
            {
                labelIndex = 0;
            }
            else if (words.Count > 1 && NumericOnly().IsMatch(words[^1].Text.Trim()))
            {
                labelIndex = words.Count - 1;
            }

            if (labelIndex >= 0)
            {
                pageLabel = words[labelIndex].Text.Trim();
            }
        }

        var kept = new List<string>();
        for (var i = 0; i < words.Count; i++)
        {
            if (i != labelIndex)
            {
                kept.Add(words[i].Text);
            }
        }

        return string.Join(" ", kept).Trim();
    }

    /// <summary>Renders body lines as markdown: lines are grouped into paragraphs on vertical gaps,
    /// wrapped lines are re-joined (de-hyphenated), and large single lines become headings.</summary>
    private static string BuildMarkdown(List<Line> bodyLines)
    {
        if (bodyLines.Count == 0)
        {
            return string.Empty;
        }

        var bodyFont = Median(bodyLines.SelectMany(l => l.Words)
            .SelectMany(w => w.Letters).Select(l => l.PointSize));
        var medianHeight = Median(bodyLines.Select(l => l.Height));
        var paragraphGap = Math.Max(medianHeight * 1.6, 1.0);

        var sb = new StringBuilder();
        var paragraph = new List<Line>();

        void Flush()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            var isHeading = paragraph.Count == 1
                            && bodyFont > 0
                            && paragraph[0].FontSize >= bodyFont * HeadingSizeRatio
                            && paragraph[0].Words.Count <= 12;

            var text = JoinWrapped(paragraph);
            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }

            sb.Append(isHeading ? $"## {text}" : text);
            paragraph.Clear();
        }

        for (var i = 0; i < bodyLines.Count; i++)
        {
            // bodyLines are ordered top-to-bottom, so the gap to the previous line is positive.
            if (i > 0 && bodyLines[i - 1].CenterY - bodyLines[i].CenterY > paragraphGap)
            {
                Flush();
            }

            paragraph.Add(bodyLines[i]);
        }

        Flush();
        return sb.ToString();
    }

    /// <summary>Joins the wrapped lines of a paragraph into one run, stitching words split across a
    /// line break by a trailing hyphen.</summary>
    private static string JoinWrapped(List<Line> lines)
    {
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length == 0)
            {
                sb.Append(line.Text);
            }
            else if (sb[^1] == '-')
            {
                sb.Length -= 1; // drop the soft hyphen and glue the word halves together
                sb.Append(line.Text);
            }
            else
            {
                sb.Append(' ').Append(line.Text);
            }
        }

        return sb.ToString();
    }

    private static double CenterY(PdfRectangle rect) => (rect.Top + rect.Bottom) / 2.0;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(v => v > 0).OrderBy(v => v).ToList();
        if (sorted.Count == 0)
        {
            return 0;
        }

        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    /// <summary>One reconstructed visual line of text.</summary>
    private sealed record Line(
        string Text,
        double CenterY,
        double Height,
        double FontSize,
        IReadOnlyList<Word> Words);
}
