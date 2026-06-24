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
///
/// Line reconstruction is column-aware: a multi-column page (e.g. a two-column book) is segmented
/// on its vertical whitespace gutter so words from different columns are never fused into one
/// scrambled line, and the columns are emitted in reading order (left-to-right, top-to-bottom).
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
            var words = page.GetWords()
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

        // Smallest gap to a line above / below this one (ignoring lines at the same height, i.e. the
        // other column's half of the same row). Infinite when there is no line on that side.
        double GapAbove(double y) => page.Lines
            .Where(l => l.CenterY > y + 0.01)
            .Select(l => l.CenterY - y)
            .DefaultIfEmpty(double.PositiveInfinity).Min();
        double GapBelow(double y) => page.Lines
            .Where(l => l.CenterY < y - 0.01)
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
    /// Reconstructs the page's visual lines in reading order. Words are clustered into rows by
    /// vertical position, each row is split at the page's column gutter (if any) so two columns
    /// sharing a vertical position become two separate lines, and the resulting lines are ordered
    /// column-by-column within each full-width band.
    /// </summary>
    private static List<Line> ReconstructLines(IReadOnlyList<Word> words)
    {
        var medianHeight = Median(words.Select(w => w.BoundingBox.Height));
        var tolerance = Math.Max(medianHeight * 0.5, 1.0);

        var gutter = DetectGutter(words, medianHeight);

        var lines = new List<Line>();
        foreach (var row in ClusterRows(words, tolerance))
        {
            lines.AddRange(SplitRowAtGutter(row, gutter));
        }

        return OrderForReading(lines);
    }

    /// <summary>Clusters words whose vertical centres are close into single visual rows, ordered
    /// top-to-bottom (PDF Y grows upward, so a larger centre is higher on the page). A row may still
    /// span several columns — <see cref="SplitRowAtGutter"/> separates those.</summary>
    private static List<List<Word>> ClusterRows(IReadOnlyList<Word> words, double tolerance)
    {
        var ordered = words
            .Select(w => new Placed(w, CenterY(w.BoundingBox)))
            .OrderByDescending(p => p.CenterY)
            .ToList();

        var buckets = new List<List<Word>>();
        var bucketCenterY = 0.0;
        foreach (var placed in ordered)
        {
            if (buckets.Count == 0 || Math.Abs(bucketCenterY - placed.CenterY) > tolerance)
            {
                buckets.Add([placed.Word]);
                bucketCenterY = placed.CenterY;
            }
            else
            {
                buckets[^1].Add(placed.Word);
            }
        }

        return buckets;
    }

    /// <summary>
    /// Detects a single dominant vertical whitespace gutter (the channel between two text columns)
    /// from the words' horizontal projection profile, or <c>null</c> when the page reads as one
    /// column. A handful of full-width lines (titles, wide tables) may cross the gutter without
    /// hiding it, because the channel is still mostly empty relative to the dense columns.
    /// Only one gutter is detected, so pages with three or more columns fall back to single-column
    /// reading order for the extra columns.
    /// </summary>
    private static (double Start, double End)? DetectGutter(IReadOnlyList<Word> words, double medianHeight)
    {
        // Too few words to reason about columns — treat as a single column (also keeps small
        // synthetic/structured pages, where a lone corner page number is the only right-side token,
        // from being mistaken for a second column).
        if (words.Count < 12)
        {
            return null;
        }

        var minLeft = words.Min(w => w.BoundingBox.Left);
        var maxRight = words.Max(w => w.BoundingBox.Right);
        var span = maxRight - minLeft;
        if (span <= 0)
        {
            return null;
        }

        const int bins = 100;
        var binWidth = span / bins;

        // density[b] = how many words horizontally overlap bin b. Column interiors are dense; a
        // gutter is a near-empty channel running down the page.
        var density = new int[bins];
        foreach (var word in words)
        {
            var first = (int)((word.BoundingBox.Left - minLeft) / binWidth);
            var last = (int)((word.BoundingBox.Right - minLeft) / binWidth);
            first = Math.Clamp(first, 0, bins - 1);
            last = Math.Clamp(last, 0, bins - 1);
            for (var b = first; b <= last; b++)
            {
                density[b]++;
            }
        }

        var peak = density.Max();
        if (peak == 0)
        {
            return null;
        }

        // A bin counts as empty when only the odd full-width line crosses it (<=10% of the peak
        // column density). Search the interior only — the outer margins are empty too.
        var emptyThreshold = peak * 0.10;
        var lo = bins / 5;
        var hi = bins - bins / 5 - 1;

        var bestStart = -1;
        var bestLength = 0;
        var runStart = -1;
        var runLength = 0;
        for (var b = lo; b <= hi; b++)
        {
            if (density[b] <= emptyThreshold)
            {
                if (runLength == 0)
                {
                    runStart = b;
                }

                runLength++;
                if (runLength > bestLength)
                {
                    bestLength = runLength;
                    bestStart = runStart;
                }
            }
            else
            {
                runLength = 0;
            }
        }

        if (bestStart < 0)
        {
            return null;
        }

        var gutterWidth = bestLength * binWidth;
        // A real gutter is at least a line-height wide; anything narrower is just inter-word spacing.
        if (gutterWidth < medianHeight)
        {
            return null;
        }

        var start = minLeft + bestStart * binWidth;
        var end = minLeft + (bestStart + bestLength) * binWidth;
        var center = (start + end) / 2.0;

        // Require substantial text on both sides — otherwise the "gutter" just separates the body
        // from a lone margin token (e.g. a page number), not two genuine columns.
        var leftCount = words.Count(w => CenterX(w.BoundingBox) < center);
        var minPerSide = words.Count * 0.15;
        if (leftCount < minPerSide || words.Count - leftCount < minPerSide)
        {
            return null;
        }

        return (start, end);
    }

    /// <summary>Splits a visual row at the column gutter. A row with words on both sides of the
    /// gutter becomes two lines; a row whose text spans the gutter (a full-width title or table)
    /// stays one full-width line; a single-column page yields one full-width line per row.</summary>
    private static IEnumerable<Line> SplitRowAtGutter(IReadOnlyList<Word> row, (double Start, double End)? gutter)
    {
        if (gutter is null)
        {
            yield return MakeLine(row, LineRegion.Full);
            yield break;
        }

        var (gutterStart, gutterEnd) = gutter.Value;

        // Any word straddling the channel makes this a genuine full-width line, so keep it whole.
        foreach (var word in row)
        {
            if (word.BoundingBox.Left < gutterEnd && word.BoundingBox.Right > gutterStart)
            {
                yield return MakeLine(row, LineRegion.Full);
                yield break;
            }
        }

        var center = (gutterStart + gutterEnd) / 2.0;
        var left = row.Where(w => CenterX(w.BoundingBox) < center).ToList();
        var right = row.Where(w => CenterX(w.BoundingBox) >= center).ToList();

        if (left.Count > 0)
        {
            yield return MakeLine(left, LineRegion.Left);
        }

        if (right.Count > 0)
        {
            yield return MakeLine(right, LineRegion.Right);
        }
    }

    /// <summary>Builds a <see cref="Line"/> from the words on one side of a row, ordered
    /// left-to-right.</summary>
    private static Line MakeLine(IReadOnlyList<Word> words, LineRegion region)
    {
        var sorted = words.OrderBy(w => w.BoundingBox.Left).ToList();
        var letters = sorted.SelectMany(w => w.Letters).ToList();
        return new Line(
            Text: string.Join(" ", sorted.Select(w => w.Text)),
            CenterY: sorted.Average(w => CenterY(w.BoundingBox)),
            Height: Median(sorted.Select(w => w.BoundingBox.Height)),
            FontSize: Median(letters.Select(l => l.PointSize)),
            Bold: IsBold(letters),
            Region: region,
            Words: sorted);
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

    /// <summary>Flattens column-split lines into reading order: within each band delimited by
    /// full-width lines, the left column is read top-to-bottom, then the right column.</summary>
    private static List<Line> OrderForReading(List<Line> lines)
    {
        var ordered = lines.OrderByDescending(l => l.CenterY).ToList();

        var result = new List<Line>(ordered.Count);
        var pendingLeft = new List<Line>();
        var pendingRight = new List<Line>();

        void FlushColumns()
        {
            result.AddRange(pendingLeft);
            result.AddRange(pendingRight);
            pendingLeft.Clear();
            pendingRight.Clear();
        }

        foreach (var line in ordered)
        {
            switch (line.Region)
            {
                case LineRegion.Left:
                    pendingLeft.Add(line);
                    break;
                case LineRegion.Right:
                    pendingRight.Add(line);
                    break;
                default:
                    // A full-width line closes the current two-column band.
                    FlushColumns();
                    result.Add(line);
                    break;
            }
        }

        FlushColumns();
        return result;
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

    /// <summary>Builds the page body, splicing reconstructed tables into the prose at their vertical
    /// position. The prose between/around tables is rebuilt band-by-band so column reading order is
    /// re-established within each band (a full-width table separates the page into bands).</summary>
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

            var markdown = BuildMarkdown(OrderForReading(band));
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
                // bodyLines are in reading order. A positive drop within a column marks a paragraph
                // break once it exceeds the gap; a non-positive drop means we have moved to a new
                // column or band (the next line sits higher up the page), which always starts a new
                // paragraph.
                var drop = bodyLines[i - 1].CenterY - bodyLines[i].CenterY;
                if (drop <= 0 || drop > paragraphGap)
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

    /// <summary>A word paired with its precomputed vertical centre, so it is calculated once
    /// rather than on every sort comparison and bucketing test.</summary>
    private readonly record struct Placed(Word Word, double CenterY);

    /// <summary>Which column region a reconstructed line belongs to within its page.</summary>
    private enum LineRegion
    {
        /// <summary>Spans the full text width (single-column page, title, or wide table row).</summary>
        Full,

        /// <summary>Left column of a two-column band.</summary>
        Left,

        /// <summary>Right column of a two-column band.</summary>
        Right
    }

    /// <summary>One reconstructed visual line of text.</summary>
    private sealed record Line(
        string Text,
        double CenterY,
        double Height,
        double FontSize,
        bool Bold,
        LineRegion Region,
        IReadOnlyList<Word> Words);
}
