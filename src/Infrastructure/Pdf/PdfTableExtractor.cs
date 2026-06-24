using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Infrastructure.Pdf;

/// <summary>
/// Reconstructs ruled tables from a PDF page as GitHub-flavoured markdown tables. Detection is
/// gated on drawn horizontal ruling lines: a region is only treated as a table when several
/// stacked horizontal rules of similar width are present (the rows). These tables draw no vertical
/// lines, so columns are recovered from the x-alignment of the cells across rows, and each word is
/// assigned to the nearest column — robust to the few-point drift between header labels and cells.
///
/// Because detection requires ruling lines, ordinary prose (which has none) is never affected.
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

    // Rules within this vertical distance are the same drawn line (deduplicated); two rules belong
    // to the same table only if the row between them is no taller than this (wrapped cells included).
    private const double SameRuleEpsilon = 2.0;
    private const double MaxRowHeight = 90.0;

    // A table needs a top rule, at least one inner rule and a bottom rule.
    private const int MinRulesForTable = 3;

    public static IReadOnlyList<DetectedTable> Detect(Page page)
    {
        var rules = HorizontalRules(page);
        if (rules.Count < MinRulesForTable)
        {
            return [];
        }

        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();

        var tables = new List<DetectedTable>();
        foreach (var group in GroupRules(rules))
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

        return tables;
    }

    /// <summary>Collects the page's horizontal ruling lines (wide, thin paths), deduplicated by
    /// vertical position with their horizontal extents merged.</summary>
    private static List<Rule> HorizontalRules(Page page)
    {
        var raw = new List<Rule>();
        foreach (var path in page.ExperimentalAccess.Paths)
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
                    raw.Add(new Rule(r.Bottom, r.Left, r.Right));
                }
            }
        }

        var merged = new List<Rule>();
        foreach (var rule in raw.OrderByDescending(r => r.Y))
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
