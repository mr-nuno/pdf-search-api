using System.Runtime.InteropServices;
using System.Text;
using Application.Common.Interfaces;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Infrastructure.Pdf;

/// <summary>
/// Extracts per-page text from a PDF using UglyToad.PdfPig. Rather than the layout-unaware
/// <see cref="Page.Text"/> property (which glues running headers, page numbers and body text into
/// one run), this reconstructs visual lines from word positions, splits off the running header and
/// printed page number, and renders the body as markdown so consumers can display it cleanly.
///
/// Line reconstruction leans on PdfPig's layout analysis: the page is segmented into text blocks and
/// those blocks are ordered for reading, so any multi-column page (a two-column book, a three-column
/// reference) reads in order instead of fusing words from different columns into scrambled lines.
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

    // A margin line is treated as a running header/footer if its text recurs in the margin band of
    // at least this many pages (catches per-chapter running heads that change every dozen pages).
    private const int RunningRepeatThreshold = 3;

    // Word-count ceilings above which a margin line is kept as body rather than treated as a running
    // header/footer. The isolation-only fallback is stricter than the recurrence path, because a
    // recurring line is almost certainly furniture whereas a one-off long line is almost certainly
    // body text (or an errata/colophon note) that merely sits near the margin.
    private const int RunningHeaderMaxWords = 12;
    private const int IsolatedHeaderMaxWords = 8;

    public IEnumerable<PdfPageText> Extract(Stream pdfStream)
    {
        using var document = PdfDocument.Open(pdfStream);

        // Pass 1: reconstruct every page's visual lines up front. Running headers/footers are
        // identified by repetition across pages, so the whole document has to be in hand before any
        // page's margin furniture can be told apart from one-off content sitting in the margins.
        var pages = new List<PageLines>();
        foreach (var page in document.GetPages())
        {
            // NearestNeighbour word grouping is more robust across varied layouts than the default
            // extractor, and feeds the page segmenter in ReconstructLines.
            var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters)
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .ToList();
            if (words.Count == 0)
            {
                continue;
            }

            // Ruled tables are reconstructed separately as markdown; their words are taken out of
            // the prose stream so they are not re-flowed into garbled lines.
            var tables = PdfTableExtractor.Detect(page);
            var proseWords = tables.Count == 0
                ? words
                : words.Where(w => !InAnyTable(w, tables)).ToList();

            pages.Add(new PageLines(page.Number, page.Height, ReconstructLines(proseWords), tables));
        }

        var runningFurniture = DetectRunningFurniture(pages);

        // Pass 2: split each page into running header/footer, page number and body.
        var results = new List<PdfPageText>(pages.Count);
        foreach (var page in pages)
        {
            var extracted = BuildPage(page, runningFurniture);
            if (extracted is not null)
            {
                results.Add(extracted);
            }
        }

        return results;
    }

    /// <summary>
    /// Finds the normalized text of lines that recur in the top/bottom margin bands across several
    /// pages — the running headers and footers. A line that appears in a margin on only one page is
    /// page-specific content (e.g. a section title that happens to sit near the top), not furniture.
    /// </summary>
    private static HashSet<string> DetectRunningFurniture(List<PageLines> pages)
    {
        var frequency = new Dictionary<string, int>();

        foreach (var page in pages)
        {
            var headerThreshold = page.Height * HeaderBandTop;
            var footerThreshold = page.Height * FooterBandBottom;

            foreach (var line in page.Lines)
            {
                var inBand = line.CenterY >= headerThreshold || line.CenterY <= footerThreshold;
                if (!inBand || IsPageNumberLine(line))
                {
                    continue;
                }

                var normalized = Normalize(line.Text);
                if (normalized.Length > 0)
                {
                    frequency[normalized] = frequency.GetValueOrDefault(normalized) + 1;
                }
            }
        }

        return frequency
            .Where(kv => kv.Value >= RunningRepeatThreshold)
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    private static PdfPageText? BuildPage(PageLines page, HashSet<string> runningFurniture)
    {
        var headerThreshold = page.Height * HeaderBandTop;
        var footerThreshold = page.Height * FooterBandBottom;

        // A running header/footer is set apart from the body by a band of whitespace. We measure that
        // against the NEAREST line on the body side (not the body block as a whole): on a dense page
        // the last body line can sit inside the margin band, but it is only a normal line-gap away
        // from the line above it, so it is body — not isolated furniture.
        var middle = page.Lines
            .Where(l => l.CenterY < headerThreshold && l.CenterY > footerThreshold)
            .ToList();
        var bodyHeight = Median((middle.Count > 0 ? middle : page.Lines).Select(l => l.Height));
        var isolationGap = Math.Max(bodyHeight * 2.5, 1.0);

        // Lines whose centres are within this of each other belong to the same horizontal row — e.g.
        // a running header and the page number sitting beside it, which the segmenter splits into two
        // separate lines at (near) the same height. Such a sibling must not count as the line's
        // nearest neighbour when measuring isolation, or the header would look "crowded" by its own
        // page number and never be recognised as furniture.
        var sameRow = Math.Max(bodyHeight * 0.6, 2.0);

        // Smallest gap to a line above / below this one (ignoring lines on the same row, i.e. another
        // column's text at the same height). Infinite when there is no line on that side.
        double GapAbove(double y) => page.Lines
            .Where(l => l.CenterY > y + sameRow)
            .Select(l => l.CenterY - y)
            .DefaultIfEmpty(double.PositiveInfinity).Min();
        double GapBelow(double y) => page.Lines
            .Where(l => l.CenterY < y - sameRow)
            .Select(l => y - l.CenterY)
            .DefaultIfEmpty(double.PositiveInfinity).Min();

        var headerParts = new List<string>();
        var bodyLines = new List<Line>();

        foreach (var line in page.Lines)
        {
            var inHeaderBand = line.CenterY >= headerThreshold;
            var inFooterBand = line.CenterY <= footerThreshold;

            if (!inHeaderBand && !inFooterBand)
            {
                bodyLines.Add(line);
                continue;
            }

            // A printed page number (a margin line of only digits) is dropped wherever it sits —
            // top, bottom, corner or centre.
            if (IsPageNumberLine(line))
            {
                continue;
            }

            var recurs = runningFurniture.Contains(Normalize(line.Text));
            var isolated = inHeaderBand
                ? GapBelow(line.CenterY) > isolationGap
                : GapAbove(line.CenterY) > isolationGap;

            // Treat the line as a running header/footer when it recurs across pages (the reliable
            // signal), or — for a single occurrence, e.g. a one-page document — when it sits alone
            // in the margin. The isolation fallback is held to a short line because a running head
            // is brief; a longer isolated margin line is far more likely to be body text, an errata
            // note, or a colophon that should stay in the body.
            var isFurniture = recurs
                ? line.Words.Count <= RunningHeaderMaxWords
                : isolated && line.Words.Count <= IsolatedHeaderMaxWords;

            if (isFurniture)
            {
                var remainder = StripCornerNumeral(line);
                if (remainder.Length > 0)
                {
                    headerParts.Add(remainder);
                }
            }
            else
            {
                bodyLines.Add(line);
            }
        }

        var header = headerParts.Count > 0 ? string.Join(" ", headerParts) : null;
        var content = BuildBodyContent(bodyLines, page.Tables);

        if (content.Length == 0 && header is null)
        {
            return null;
        }

        return new PdfPageText(page.Number, content, header);
    }

    /// <summary>True if every token on the line is a numeral — i.e. the line is just a printed page
    /// number (in a top/bottom corner or centred), not running-header prose.</summary>
    private static bool IsPageNumberLine(Line line) =>
        line.Words.Count > 0 && line.Words.All(w => IsCornerNumeral(w.Text));

    /// <summary>Collapses a line to a comparable key (single-spaced, lower-cased) so the same
    /// running header/footer matches across pages despite incidental spacing differences.</summary>
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    /// <summary>
    /// Reconstructs the page's visual lines in reading order using PdfPig's layout analysis: the
    /// page is segmented into text blocks (any number of columns), the blocks are ordered for
    /// reading, and each block's lines are read top-to-bottom. Each line is tagged with its block
    /// index so paragraph breaks can fall on block boundaries.
    /// </summary>
    private static List<Line> ReconstructLines(IReadOnlyList<Word> words)
    {
        if (words.Count == 0)
        {
            return [];
        }

        var blocks = RecursiveXYCut.Instance.GetBlocks(words).ToList();
        var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks)
            .OrderBy(b => b.ReadingOrder)
            .ToList();

        var lines = new List<Line>();
        for (var block = 0; block < ordered.Count; block++)
        {
            // PDF Y grows upward, so a larger centre is higher on the page: read top-to-bottom.
            foreach (var textLine in ordered[block].TextLines.OrderByDescending(l => CenterY(l.BoundingBox)))
            {
                lines.Add(MakeLine(textLine, block));
            }
        }

        return lines;
    }

    /// <summary>Builds a <see cref="Line"/> from a segmented text line, tagged with the reading-order
    /// index of the block it came from.</summary>
    private static Line MakeLine(TextLine textLine, int block)
    {
        var words = textLine.Words.OrderBy(w => w.BoundingBox.Left).ToList();
        var letters = words.SelectMany(w => w.Letters).ToList();
        return new Line(
            Text: string.Join(" ", words.Select(w => w.Text)),
            CenterY: words.Average(w => CenterY(w.BoundingBox)),
            Height: Median(words.Select(w => w.BoundingBox.Height)),
            FontSize: Median(letters.Select(l => l.PointSize)),
            Bold: IsBold(letters),
            Block: block,
            Words: words);
    }

    /// <summary>True if most of the line is set in a bold/black/heavy face — a font-weight signal
    /// that a short line is a heading even when it is not set in a larger size.</summary>
    private static bool IsBold(IReadOnlyList<Letter> letters)
    {
        if (letters.Count == 0)
        {
            return false;
        }

        var bold = 0;
        foreach (var letter in letters)
        {
            if (IsBoldFontName(letter.FontName))
            {
                bold++;
            }
        }

        return bold * 2 >= letters.Count;
    }

    private static bool IsBoldFontName(string? fontName) =>
        fontName is not null
        && (fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase)
            || fontName.Contains("Black", StringComparison.OrdinalIgnoreCase)
            || fontName.Contains("Heavy", StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Builds the page body, splicing reconstructed tables into the prose at their vertical
    /// position. The prose lines arrive already in reading order, so each band between/around tables
    /// keeps that order (a full-width table separates the page into bands).</summary>
    private static string BuildBodyContent(List<Line> bodyLines, IReadOnlyList<PdfTableExtractor.DetectedTable> tables)
    {
        if (tables.Count == 0)
        {
            return BuildMarkdown(bodyLines);
        }

        var parts = new List<string>();
        var upper = double.PositiveInfinity;

        void AppendProse(double lowerExclusive, double upperInclusive)
        {
            var band = bodyLines
                .Where(l => l.CenterY > lowerExclusive && l.CenterY <= upperInclusive)
                .ToList();
            if (band.Count == 0)
            {
                return;
            }

            var markdown = BuildMarkdown(band);
            if (markdown.Length > 0)
            {
                parts.Add(markdown);
            }
        }

        foreach (var table in tables.OrderByDescending(t => t.Top))
        {
            AppendProse(table.Top, upper);
            parts.Add(table.Markdown);
            upper = table.Top;
        }

        AppendProse(double.NegativeInfinity, upper);

        return string.Join("\n\n", parts);
    }

    /// <summary>Renders body lines as markdown: lines are grouped into paragraphs on block
    /// boundaries and vertical gaps, wrapped lines are re-joined (de-hyphenated), and large single
    /// lines become headings.</summary>
    private static string BuildMarkdown(List<Line> bodyLines)
    {
        if (bodyLines.Count == 0)
        {
            return string.Empty;
        }

        var bodyFont = Median(bodyLines.SelectMany(l => l.Words)
            .SelectMany(w => w.Letters).Select(l => l.PointSize));
        var medianHeight = Median(bodyLines.Select(l => l.Height));

        // A new paragraph is marked by a vertical drop noticeably larger than normal line leading.
        // Real-world leading runs ~1.5-1.7x the glyph height while paragraph/section gaps run ~2x
        // leading or more, so a threshold of ~2.5x the glyph height sits between the two: wrapped
        // lines within a paragraph stay joined (so de-hyphenation can fire) while genuine paragraph
        // breaks still split.
        var paragraphGap = Math.Max(medianHeight * 2.5, 1.0);

        var sb = new StringBuilder();
        var paragraph = new List<Line>();

        void Flush()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            var head = paragraph[0];
            var isHeading = paragraph.Count == 1
                            && bodyFont > 0
                            && head.Words.Count <= 12
                            // Enlarged relative to the body, or a bold face that is at least body
                            // size (so a same-size bold section title is promoted, but a smaller
                            // bold label — e.g. a table column header — is not).
                            && (head.FontSize >= bodyFont * HeadingSizeRatio
                                || (head.Bold && head.FontSize >= bodyFont * 0.99));

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
            if (i > 0)
            {
                var previous = bodyLines[i - 1];
                var current = bodyLines[i];

                // A change of segmented block always starts a new paragraph (a separate column or a
                // visually distinct group). Within a block, bodyLines are top-to-bottom: a positive
                // drop larger than the gap marks a paragraph break, and a non-positive drop means we
                // have moved to a line that sits higher up the page, which also starts a paragraph.
                var drop = previous.CenterY - current.CenterY;
                if (current.Block != previous.Block || drop <= 0 || drop > paragraphGap)
                {
                    Flush();
                }
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

    private static double CenterX(PdfRectangle rect) => (rect.Left + rect.Right) / 2.0;

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

    /// <summary>A page's reconstructed prose lines and any ruled tables, plus the data needed to
    /// classify its margins.</summary>
    private sealed record PageLines(
        int Number,
        double Height,
        List<Line> Lines,
        IReadOnlyList<PdfTableExtractor.DetectedTable> Tables);

    /// <summary>True if the word falls inside a detected table's region (so it belongs to the table
    /// markdown, not the prose stream).</summary>
    private static bool InAnyTable(Word word, IReadOnlyList<PdfTableExtractor.DetectedTable> tables)
    {
        var x = (word.BoundingBox.Left + word.BoundingBox.Right) / 2.0;
        var y = CenterY(word.BoundingBox);
        foreach (var t in tables)
        {
            if (y >= t.Bottom && y <= t.Top && x >= t.Left && x <= t.Right)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One reconstructed visual line of text.</summary>
    private sealed record Line(
        string Text,
        double CenterY,
        double Height,
        double FontSize,
        bool Bold,
        int Block,
        IReadOnlyList<Word> Words);
}
