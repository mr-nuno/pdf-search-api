using System.Runtime.InteropServices;
using System.Text;
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
public sealed class PdfPigTextExtractor : IPdfTextExtractor
{
    // Fraction of page height treated as the top/bottom margin bands where running headers,
    // footers and page numbers live. A line whose vertical centre is above the header threshold
    // (or below the footer threshold) is treated as margin furniture, not body.
    private const double HeaderBandTop = 0.90;
    private const double FooterBandBottom = 0.10;

    // A single body line whose font is at least this much larger than the page's body text is
    // promoted to a markdown heading.
    private const double HeadingSizeRatio = 1.3;

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
        var words = page.GetWords()
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

        foreach (var line in lines)
        {
            var inHeaderBand = line.CenterY >= headerThreshold;
            var inFooterBand = line.CenterY <= footerThreshold;

            if (inHeaderBand || inFooterBand)
            {
                // Strip any numeric-only corner token (the printed page number) so it does not
                // bleed into the running header. Footer prose is dropped entirely.
                var remainder = StripCornerNumeral(line);
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

        return new PdfPageText(page.Number, content, header);
    }

    /// <summary>Clusters words whose vertical centres are close into single visual lines,
    /// ordered top-to-bottom and, within each line, left-to-right.</summary>
    private static List<Line> GroupIntoLines(IReadOnlyList<Word> words)
    {
        var medianHeight = Median(words.Select(w => w.BoundingBox.Height));
        var tolerance = Math.Max(medianHeight * 0.5, 1.0);

        // Project each word to its vertical centre once, then sort top-to-bottom (PDF Y grows
        // upward, so larger centre = higher on the page) so words of one line are contiguous and a
        // new line starts on a vertical jump.
        var ordered = words
            .Select(w => new Placed(w, CenterY(w.BoundingBox)))
            .OrderByDescending(p => p.CenterY)
            .ToList();

        var buckets = new List<List<Placed>>();
        var bucketCenterY = 0.0;
        foreach (var placed in ordered)
        {
            if (buckets.Count == 0 || Math.Abs(bucketCenterY - placed.CenterY) > tolerance)
            {
                buckets.Add([placed]);
                bucketCenterY = placed.CenterY;
            }
            else
            {
                buckets[^1].Add(placed);
            }
        }

        var lines = new List<Line>(buckets.Count);
        foreach (var bucket in buckets)
        {
            var sorted = bucket.OrderBy(p => p.Word.BoundingBox.Left).Select(p => p.Word).ToList();
            lines.Add(new Line(
                Text: string.Join(" ", sorted.Select(w => w.Text)),
                CenterY: bucket.Average(p => p.CenterY),
                Height: Median(sorted.Select(w => w.BoundingBox.Height)),
                FontSize: Median(sorted.SelectMany(w => w.Letters).Select(l => l.PointSize)),
                Words: sorted));
        }

        return lines;
    }

    /// <summary>Strips a numeric-only corner token (printed page number) from a margin line and
    /// returns the remaining text. Only the first and last token are candidates, so a numeral
    /// embedded between header words (e.g. the "8" in "KAPITEL 8") is preserved.</summary>
    private static string StripCornerNumeral(Line line)
    {
        var words = line.Words;
        var stripIndex = -1;

        if (IsCornerNumeral(words[0].Text))
        {
            stripIndex = 0;
        }
        else if (words.Count > 1 && IsCornerNumeral(words[^1].Text))
        {
            stripIndex = words.Count - 1;
        }

        // Nothing to strip — the line text is already the space-joined words.
        if (stripIndex < 0)
        {
            return line.Text;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            if (i == stripIndex)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(words[i].Text);
        }

        return sb.ToString();
    }

    /// <summary>True if the token (ignoring surrounding whitespace) is a 1–4 digit numeral — the
    /// shape of a printed page number. Span-based so no trimmed string is allocated per check.</summary>
    private static bool IsCornerNumeral(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        if (text.Length is < 1 or > 4)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
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

    private static double Median(IEnumerable<double> values) =>
        // ToList into a private buffer the span overload may reorder/compact in place.
        Median(CollectionsMarshal.AsSpan(values.ToList()));

    private static double Median(Span<double> values)
    {
        // Compact the positive values to the front in place, then sort only that prefix — avoids
        // the filtered-list + OrderBy allocation the IEnumerable path used to make per call.
        var count = 0;
        foreach (var value in values)
        {
            if (value > 0)
            {
                values[count++] = value;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        var positives = values[..count];
        positives.Sort();

        var mid = count / 2;
        return count % 2 == 0 ? (positives[mid - 1] + positives[mid]) / 2.0 : positives[mid];
    }

    /// <summary>A word paired with its precomputed vertical centre, so it is calculated once
    /// rather than on every sort comparison and bucketing test.</summary>
    private readonly record struct Placed(Word Word, double CenterY);

    /// <summary>One reconstructed visual line of text.</summary>
    private sealed record Line(
        string Text,
        double CenterY,
        double Height,
        double FontSize,
        IReadOnlyList<Word> Words);
}
