using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;

namespace Infrastructure.Pdf;

/// <summary>
/// Reconstructs tables from a PDF page as GitHub-flavoured markdown tables. A region is only
/// treated as a table when its rows are marked by a clear visual delimiter — either drawn
/// horizontal ruling lines, or shaded (filled) row backgrounds (zebra striping) — so ordinary
/// prose, which has neither, is never affected. Several stacked row boundaries of similar width
/// are required (the rows). Columns are recovered from the x-alignment of the cells across rows,
/// and each word is assigned to the nearest column — robust to the few-point drift between header
/// labels and cells.
///
/// Multiple tables can sit side by side on a page; their row boundaries are first partitioned into
/// horizontally-separated regions so they are reconstructed independently rather than fused.
/// </summary>
internal static class PdfTableExtractor
{
    /// <summary>A reconstructed table and the page region it occupies (so its words can be excluded
    /// from prose reconstruction and its markdown spliced back in at the right vertical position).</summary>
    internal sealed record DetectedTable(double Top, double Bottom, double Left, double Right, string Markdown);

    private sealed record Rule(double Y, double Left, double Right);

    // A horizontal ruling line is a path whose bounding box is wide and very thin.
    private const double MinRuleWidth = 40.0;
    private const double MaxRuleThickness = 2.5;

    // A shaded row background is a filled rectangle as tall as a table row. A row's cell text often
    // wraps to several lines, so the band can be a good deal taller than a single line of body text;
    // the ceiling only rules out multi-paragraph callout boxes / sidebars (which run well past this),
    // not tall wrapped rows. Its top and bottom edges act as row boundaries. A lone over-tall box is
    // still harmless: it yields only two rules (< MinRulesForTable) and so never forms a table.
    private const double MinRowBandHeight = 6.0;
    private const double MaxRowBandHeight = 80.0;

    // A shaded row's fill must be a light background tint: dark enough not to be page white, light
    // enough not to be a black title bar or a saturated colour tag (those are not row backgrounds).
    private const double MinShadeLuminance = 0.25;
    private const double MaxShadeLuminance = 0.97;

    // The cells of one shaded row abut (a single visual band), so two filled cell-boxes join the
    // same row only when they vertically overlap and are no further apart horizontally than this —
    // which keeps the cells of side-by-side tables (separated by a gutter) in different rows.
    private const double SameRowCellGap = 6.0;

    // Rules within this vertical distance are the same drawn line (deduplicated); two rules belong
    // to the same table only if the row between them is no taller than this (wrapped cells included).
    private const double SameRuleEpsilon = 2.0;
    private const double MaxRowHeight = 90.0;

    // A table needs a top rule, at least one inner rule and a bottom rule.
    private const int MinRulesForTable = 3;

    public static IReadOnlyList<DetectedTable> Detect(Page page)
    {
        // Row boundaries come from two visual delimiters: thin drawn ruling lines and the top/bottom
        // edges of shaded row backgrounds. A table with a stroked grid over a narrow column and
        // shading across the full width thus reaches the full width, and a table with shading but no
        // rules is still recovered.
        var rawRules = new List<Rule>([.. HorizontalRules(page), .. ShadedRowRules(page)]);
        if (rawRules.Count < MinRulesForTable)
        {
            return [];
        }

        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();

        var tables = new List<DetectedTable>();

        // Side-by-side tables are split into horizontally-separated regions FIRST, before the rules
        // are deduplicated by vertical position: their rows sit at the same Y, so a global Y-merge
        // would otherwise fuse the three tables' co-aligned boundaries into full-width rules and
        // scramble them into one table.
        foreach (var region in ClusterByColumn(rawRules))
        {
            var merged = MergeByPosition(region);
            foreach (var group in GroupRules(merged))
            {
                if (group.Count < MinRulesForTable)
                {
                    continue;
                }

                var table = BuildTable(words, group);
                if (table is not null)
                {
                    tables.Add(table);
                }
            }
        }

        return tables;
    }

    /// <summary>Collects the page's horizontal ruling lines: subpaths whose bounding box is wide and
    /// very thin. Returned raw (not deduplicated) — <see cref="MergeByPosition"/> collapses coincident
    /// lines once they are pooled with the shaded-row edges.</summary>
    private static List<Rule> HorizontalRules(Page page)
    {
        var rules = new List<Rule>();
        foreach (var path in page.Paths)
        {
            foreach (var subpath in path)
            {
                var box = subpath.GetBoundingRectangle();
                if (box is null)
                {
                    continue;
                }

                var r = box.Value;
                if (r.Width >= MinRuleWidth && r.Height <= MaxRuleThickness)
                {
                    rules.Add(new Rule(r.Bottom, r.Left, r.Right));
                }
            }
        }

        return rules;
    }

    /// <summary>Collects row boundaries from shaded row backgrounds: filled rectangles set in a light
    /// background tint whose height is a text-row tall. The cells of one shaded row are filled as
    /// separate boxes (e.g. a narrow index column beside a wide text column), so boxes are grouped
    /// into rows by overlapping vertical position and each row's horizontal extent is unioned; the
    /// row's top and bottom edges are then emitted as rules. With alternating (zebra) shading every
    /// row is still bounded — the gap between one shaded rect's bottom and the next's top is exactly
    /// the unshaded row between them.</summary>
    private static List<Rule> ShadedRowRules(Page page)
    {
        var boxes = new List<PdfRectangle>();
        foreach (var path in page.Paths)
        {
            if (!path.IsFilled || !IsRowShade(path.FillColor))
            {
                continue;
            }

            foreach (var subpath in path)
            {
                var box = subpath.GetBoundingRectangle();
                if (box is null)
                {
                    continue;
                }

                var r = box.Value;
                if (r.Width >= MinRuleWidth / 2 && r.Height >= MinRowBandHeight && r.Height <= MaxRowBandHeight)
                {
                    boxes.Add(r);
                }
            }
        }

        var rules = new List<Rule>();
        foreach (var row in GroupBoxesIntoRows(boxes))
        {
            var left = row.Min(b => b.Left);
            var right = row.Max(b => b.Right);
            if (right - left < MinRuleWidth)
            {
                continue; // a lone narrow shaded cell is not a row
            }

            rules.Add(new Rule(row.Max(b => b.Top), left, right));
            rules.Add(new Rule(row.Min(b => b.Bottom), left, right));
        }

        return rules;
    }

    /// <summary>Groups filled cell-boxes into shaded rows: a box joins a row when it vertically
    /// overlaps and horizontally abuts one of the row's boxes. The horizontal-adjacency test keeps
    /// the cells of one table's row together (they share an edge) while separating side-by-side
    /// tables, whose cells are divided by a gutter wider than <see cref="SameRowCellGap"/>.</summary>
    private static List<List<PdfRectangle>> GroupBoxesIntoRows(List<PdfRectangle> boxes)
    {
        static bool VerticallyOverlap(PdfRectangle a, PdfRectangle b) =>
            Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom) > 0;

        static bool HorizontallyAdjacent(PdfRectangle a, PdfRectangle b) =>
            Math.Max(a.Left, b.Left) - Math.Min(a.Right, b.Right) <= SameRowCellGap;

        var rows = new List<List<PdfRectangle>>();
        foreach (var box in boxes.OrderByDescending(b => (b.Top + b.Bottom) / 2.0))
        {
            var row = rows.FirstOrDefault(r => r.Any(b => VerticallyOverlap(b, box) && HorizontallyAdjacent(b, box)));
            if (row is null)
            {
                rows.Add([box]);
            }
            else
            {
                row.Add(box);
            }
        }

        return rows;
    }

    /// <summary>True if a fill colour is a light background tint — dark enough not to be page white,
    /// light enough not to be a black title bar or a saturated colour tag (which are not rows).</summary>
    private static bool IsRowShade(IColor? color)
    {
        if (color is null)
        {
            return false;
        }

        var (r, g, b) = color.ToRGBValues();
        var luminance = 0.2126 * (double)r + 0.7152 * (double)g + 0.0722 * (double)b;
        return luminance is > MinShadeLuminance and < MaxShadeLuminance;
    }

    /// <summary>Pools rules (from ruling lines and shaded-row edges), sorts them top-to-bottom and
    /// deduplicates by vertical position, unioning the horizontal extents of coincident rules — so a
    /// grid line drawn over only one column and a shade spanning the full row collapse into one rule
    /// covering the full width.</summary>
    private static List<Rule> MergeByPosition(List<Rule> rules)
    {
        var merged = new List<Rule>();
        foreach (var rule in rules.OrderByDescending(r => r.Y))
        {
            if (merged.Count > 0 && merged[^1].Y - rule.Y <= SameRuleEpsilon)
            {
                var last = merged[^1];
                merged[^1] = last with
                {
                    Left = Math.Min(last.Left, rule.Left),
                    Right = Math.Max(last.Right, rule.Right),
                };
            }
            else
            {
                merged.Add(rule);
            }
        }

        return merged;
    }

    /// <summary>Partitions rules into horizontally-separated regions (tables sitting side by side on
    /// the page), so their row boundaries — interleaved when the whole page is sorted by Y — are not
    /// fused. Rules are clustered greedily by horizontal overlap against a running extent. Identity
    /// for a page with a single column of tables.</summary>
    private static List<List<Rule>> ClusterByColumn(List<Rule> rules)
    {
        var regions = new List<(double Left, double Right, List<Rule> Rules)>();

        foreach (var rule in rules.OrderBy(r => r.Left))
        {
            var region = regions.FirstOrDefault(x => Overlap(new Rule(0, x.Left, x.Right), rule) > 0.5);
            if (region.Rules is null)
            {
                regions.Add((rule.Left, rule.Right, [rule]));
            }
            else
            {
                region.Rules.Add(rule);
                var i = regions.IndexOf(region);
                regions[i] = (Math.Min(region.Left, rule.Left), Math.Max(region.Right, rule.Right), region.Rules);
            }
        }

        // Each region's rules are returned top-to-bottom, as GroupRules expects.
        return regions.Select(r => r.Rules.OrderByDescending(x => x.Y).ToList()).ToList();
    }

    /// <summary>Groups vertically-stacked rules of similar horizontal extent into table candidates.</summary>
    private static List<List<Rule>> GroupRules(List<Rule> rules)
    {
        var groups = new List<List<Rule>>();
        var current = new List<Rule>();

        foreach (var rule in rules) // already sorted top-to-bottom
        {
            if (current.Count == 0)
            {
                current.Add(rule);
                continue;
            }

            var previous = current[^1];
            var sameTable = previous.Y - rule.Y <= MaxRowHeight && Overlap(previous, rule) > 0.5;

            if (sameTable)
            {
                current.Add(rule);
            }
            else
            {
                groups.Add(current);
                current = [rule];
            }
        }

        if (current.Count > 0)
        {
            groups.Add(current);
        }

        return groups;
    }

    /// <summary>Fraction of the narrower rule's width that overlaps the wider rule horizontally.</summary>
    private static double Overlap(Rule a, Rule b)
    {
        var shared = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        if (shared <= 0)
        {
            return 0;
        }

        var narrower = Math.Min(a.Right - a.Left, b.Right - b.Left);
        return narrower <= 0 ? 0 : shared / narrower;
    }

    private static DetectedTable? BuildTable(IReadOnlyList<Word> words, List<Rule> rules)
    {
        var topRule = rules[0].Y;
        var bottom = rules[^1].Y;
        var left = rules.Min(r => r.Left);
        var right = rules.Max(r => r.Right);
        var tolerance = ColumnTolerance(words, topRule, bottom, left, right);

        bool InRegionX(Word w) => CenterX(w.BoundingBox) >= left - tolerance
                                  && CenterX(w.BoundingBox) <= right + tolerance;

        var rowHeight = MedianHeight(words.Where(w => CenterY(w.BoundingBox) < topRule
                                                      && CenterY(w.BoundingBox) > bottom && InRegionX(w)));
        var headerLookup = Math.Max(rowHeight * 2.5, 12.0);

        // The header row is the line of labels sitting just above the top rule; if there is none,
        // the first ruled band acts as the header.
        var headerWords = words
            .Where(w => InRegionX(w) && CenterY(w.BoundingBox) > topRule && CenterY(w.BoundingBox) <= topRule + headerLookup)
            .ToList();

        int dataRuleStart;
        double top; // effective top, including a header drawn above the top rule
        if (headerWords.Count >= 2)
        {
            dataRuleStart = 0;
            top = headerWords.Max(w => w.BoundingBox.Top);
        }
        else
        {
            headerWords = WordsInBand(words, rules[1].Y, rules[0].Y, InRegionX);
            dataRuleStart = 1;
            top = topRule;
        }

        var dataBands = new List<List<Word>>();
        for (var i = dataRuleStart; i < rules.Count - 1; i++)
        {
            var band = WordsInBand(words, rules[i + 1].Y, rules[i].Y, InRegionX);
            if (band.Count > 0)
            {
                dataBands.Add(band);
            }
        }

        // A zebra table whose final row is unshaded has no rule beneath it (only the shaded rows
        // draw edges). Capture that trailing row from the words just below the last rule, walking
        // down only while consecutive lines stay within one row's leading: the table's own wrapped
        // last row is taken, but a heading or paragraph that follows — set off by a larger gap — is
        // not. Then extend the table region down to enclose the captured row.
        var leading = Math.Max(rowHeight * 2.5, 14.0);
        var below = words
            .Where(w => InRegionX(w) && CenterY(w.BoundingBox) < bottom && CenterY(w.BoundingBox) > bottom - MaxRowHeight)
            .ToList();
        var trailing = new List<Word>();
        var previousY = bottom;
        foreach (var line in GroupLines(below, rowHeight))
        {
            var lineY = line.Average(w => CenterY(w.BoundingBox));
            if (previousY - lineY > leading)
            {
                break;
            }

            trailing.AddRange(line);
            previousY = lineY;
        }

        if (trailing.Count > 0)
        {
            dataBands.Add(trailing);
            bottom = trailing.Min(w => w.BoundingBox.Bottom);
        }

        if (dataBands.Count == 0)
        {
            return null;
        }

        // Columns are the x-positions where cells consistently start across the header and data rows.
        var bands = new List<List<Word>> { headerWords };
        bands.AddRange(dataBands);
        var columnStarts = DetectColumns(bands, rowHeight);
        if (columnStarts.Count < 2)
        {
            return null;
        }

        var header = RowCells(headerWords, columnStarts, rowHeight);
        var rows = dataBands.Select(b => RowCells(b, columnStarts, rowHeight)).ToList();

        return new DetectedTable(top, bottom, left, right, Render(header, rows, columnStarts.Count));
    }

    /// <summary>Finds the column start x-positions: collect the left edge of every word that begins
    /// a cell (the first word on its line, or a word preceded by a column-sized gap — NOT an ordinary
    /// word-space, so a wrapped cell's interior words don't masquerade as columns), cluster those,
    /// keep the clusters supported by most rows, and merge clusters closer than a cell's width (so a
    /// two-token cell like "5 silver" is one column, not two).</summary>
    private static List<double> DetectColumns(List<List<Word>> bands, double rowHeight)
    {
        var clusterTolerance = Math.Max(rowHeight * 0.5, 2.5);
        var minColumnWidth = Math.Max(rowHeight * 1.5, 12.0);
        var columnGap = Math.Max(rowHeight * 1.2, 8.0);

        var entries = new List<(double Left, int Band)>();
        for (var b = 0; b < bands.Count; b++)
        {
            foreach (var left in CellStarts(bands[b], rowHeight, columnGap))
            {
                entries.Add((left, b));
            }
        }

        if (entries.Count == 0)
        {
            return [];
        }

        entries.Sort((x, y) => x.Left.CompareTo(y.Left));

        var clusters = new List<(double MinLeft, double LastLeft, HashSet<int> Bands)>();
        foreach (var (leftX, band) in entries)
        {
            if (clusters.Count > 0 && leftX - clusters[^1].LastLeft <= clusterTolerance)
            {
                var c = clusters[^1];
                c.Bands.Add(band);
                clusters[^1] = (c.MinLeft, leftX, c.Bands);
            }
            else
            {
                clusters.Add((leftX, leftX, [band]));
            }
        }

        var support = Math.Max(2, (int)Math.Ceiling(bands.Count * 0.5));

        var starts = new List<double>();
        foreach (var cluster in clusters.Where(c => c.Bands.Count >= support).OrderBy(c => c.MinLeft))
        {
            if (starts.Count > 0 && cluster.MinLeft - starts[^1] < minColumnWidth)
            {
                continue; // merge into the previous column
            }

            starts.Add(cluster.MinLeft);
        }

        return starts;
    }

    /// <summary>Assigns a band's words to columns by nearest column start, and joins each cell.</summary>
    private static string[] RowCells(List<Word> words, List<double> columnStarts, double rowHeight)
    {
        var buckets = new List<Word>[columnStarts.Count];
        for (var i = 0; i < buckets.Length; i++)
        {
            buckets[i] = [];
        }

        foreach (var word in words)
        {
            buckets[NearestColumn(word.BoundingBox.Left, columnStarts)].Add(word);
        }

        var cells = new string[columnStarts.Count];
        for (var i = 0; i < buckets.Length; i++)
        {
            cells[i] = JoinCell(buckets[i], rowHeight);
        }

        return cells;
    }

    private static int NearestColumn(double leftX, List<double> columnStarts)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < columnStarts.Count; i++)
        {
            var distance = Math.Abs(leftX - columnStarts[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    /// <summary>The left edge of each word that starts a cell within a band: the first word on a
    /// line, or any word preceded by a column-sized gap (not an ordinary inter-word space).</summary>
    private static IEnumerable<double> CellStarts(List<Word> words, double rowHeight, double columnGap)
    {
        foreach (var line in GroupLines(words, rowHeight))
        {
            var previousRight = double.NegativeInfinity;
            foreach (var word in line)
            {
                if (word.BoundingBox.Left - previousRight > columnGap)
                {
                    yield return word.BoundingBox.Left;
                }

                previousRight = word.BoundingBox.Right;
            }
        }
    }

    /// <summary>Joins a cell's words into one string: order into visual lines (top-to-bottom, then
    /// left-to-right within a line), then stitch the lines, de-hyphenating words split across a wrap.</summary>
    private static string JoinCell(List<Word> words, double rowHeight)
    {
        if (words.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var line in GroupLines(words, rowHeight))
        {
            foreach (var word in line)
            {
                if (sb.Length == 0)
                {
                    sb.Append(word.Text);
                }
                else if (sb[^1] == '-')
                {
                    sb.Length -= 1; // glue a hyphen-split word back together
                    sb.Append(word.Text);
                }
                else
                {
                    sb.Append(' ').Append(word.Text);
                }
            }
        }

        return sb.ToString().Replace("|", "\\|");
    }

    /// <summary>Groups words into visual lines (clustered by vertical centre), ordered top-to-bottom,
    /// each line ordered left-to-right.</summary>
    private static List<List<Word>> GroupLines(List<Word> words, double rowHeight)
    {
        var lineTolerance = Math.Max(rowHeight * 0.5, 2.0);
        var lines = new List<List<Word>>();
        var lineY = double.NaN;

        foreach (var word in words.OrderByDescending(w => CenterY(w.BoundingBox)))
        {
            var y = CenterY(word.BoundingBox);
            if (lines.Count == 0 || Math.Abs(lineY - y) > lineTolerance)
            {
                lines.Add([word]);
                lineY = y;
            }
            else
            {
                lines[^1].Add(word);
            }
        }

        foreach (var line in lines)
        {
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        return lines;
    }

    private static List<Word> WordsInBand(IReadOnlyList<Word> words, double lowerY, double upperY, Func<Word, bool> inRegionX) =>
        words.Where(w => inRegionX(w)
                         && CenterY(w.BoundingBox) > lowerY
                         && CenterY(w.BoundingBox) < upperY)
            .ToList();

    private static string Render(string[] header, List<string[]> rows, int columns)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", Cells(header, columns))).Append(" |\n");
        sb.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", columns))).Append(" |");

        foreach (var row in rows)
        {
            sb.Append("\n| ").Append(string.Join(" | ", Cells(row, columns))).Append(" |");
        }

        return sb.ToString();
    }

    private static IEnumerable<string> Cells(string[] cells, int columns)
    {
        for (var i = 0; i < columns; i++)
        {
            yield return i < cells.Length ? cells[i] : string.Empty;
        }
    }

    /// <summary>Tolerance for treating a word as inside the table horizontally — about half a column
    /// gap, scaled to the table's text size.</summary>
    private static double ColumnTolerance(IReadOnlyList<Word> words, double top, double bottom, double left, double right)
    {
        var rowHeight = MedianHeight(words.Where(w =>
            CenterY(w.BoundingBox) < top && CenterY(w.BoundingBox) > bottom
            && CenterX(w.BoundingBox) >= left && CenterX(w.BoundingBox) <= right));
        return Math.Max(rowHeight, 6.0);
    }

    private static double CenterY(PdfRectangle rect) => (rect.Top + rect.Bottom) / 2.0;

    private static double CenterX(PdfRectangle rect) => (rect.Left + rect.Right) / 2.0;

    private static double MedianHeight(IEnumerable<Word> words)
    {
        var heights = words.Select(w => w.BoundingBox.Height).Where(h => h > 0).OrderBy(h => h).ToList();
        if (heights.Count == 0)
        {
            return 0;
        }

        var mid = heights.Count / 2;
        return heights.Count % 2 == 0 ? (heights[mid - 1] + heights[mid]) / 2.0 : heights[mid];
    }
}
