namespace TextPeach.Services;

public enum DiffKind { Same, Added, Removed }

/// <summary>One line of a diff: <see cref="OldNo"/>/<see cref="NewNo"/> are 1-based, 0 when the side has no line.</summary>
public readonly record struct DiffRow(DiffKind Kind, int OldNo, int NewNo, string Text);

/// <summary>A row of the side-by-side view; a null side is filler that keeps both columns aligned.</summary>
public readonly record struct SidePair(DiffRow? Left, DiffRow? Right);

/// <summary>Gutter state of a line of the working file, compared with the last commit.</summary>
[Flags]
public enum LineMark : byte { None = 0, Added = 1, Modified = 2, DeletedAbove = 4, DeletedBelow = 8 }

/// <summary>
/// Line diff (Myers, after trimming the common start and end) between the committed and the current text, and the
/// views built from it: inline rows, side-by-side pairs and gutter marks. "\r\n" and "\n" compare equal, so a
/// checkout with autocrlf does not mark every line.
/// </summary>
public static class LineDiff
{
    /// <summary>Above this many edits the middle is shown as one replaced block instead of a minimal diff.</summary>
    private const int MaxEdits = 2000;

    public static string[] SplitLines(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].EndsWith('\r')) lines[i] = lines[i][..^1];
        return lines;
    }

    public static List<DiffRow> Compute(string oldText, string newText) => Compute(SplitLines(oldText), SplitLines(newText));

    public static List<DiffRow> Compute(string[] a, string[] b)
    {
        int start = 0, endA = a.Length, endB = b.Length;
        while (start < endA && start < endB && a[start] == b[start]) start++;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1]) { endA--; endB--; }

        var rows = new List<DiffRow>(Math.Max(a.Length, b.Length) + 8);
        for (int i = 0; i < start; i++) rows.Add(new(DiffKind.Same, i + 1, i + 1, b[i]));
        rows.AddRange(Middle(a, b, start, endA, endB));
        for (int i = endA, j = endB; i < a.Length; i++, j++) rows.Add(new(DiffKind.Same, i + 1, j + 1, b[j]));
        return rows;
    }

    /// <summary>Myers' O((N+M)·D) diff of a[start..endA] against b[start..endB].</summary>
    private static List<DiffRow> Middle(string[] a, string[] b, int start, int endA, int endB)
    {
        int n = endA - start, m = endB - start;
        var result = new List<DiffRow>();
        if (n == 0 && m == 0) return result;

        // Compare ints, not strings.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string s) { if (!ids.TryGetValue(s, out var id)) ids[s] = id = ids.Count; return id; }
        var x0 = new int[n];
        var y0 = new int[m];
        for (int i = 0; i < n; i++) x0[i] = Id(a[start + i]);
        for (int j = 0; j < m; j++) y0[j] = Id(b[start + j]);

        int max = n + m, off = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        int found = -1;
        for (int d = 0; d <= Math.Min(max, MaxEdits) && found < 0; d++)
        {
            for (int k = -d; k <= d; k += 2)
            {
                int x = d == 0 ? 0 : k == -d || (k != d && v[off + k - 1] < v[off + k + 1]) ? v[off + k + 1] : v[off + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && x0[x] == y0[y]) { x++; y++; }
                v[off + k] = x;
                if (x >= n && y >= m) { found = d; break; }
            }
            var snap = new int[2 * d + 1];
            Array.Copy(v, off - d, snap, 0, 2 * d + 1);
            trace.Add(snap);
        }

        if (found < 0)
        {
            // Too different to be worth a minimal diff: everything in between was replaced.
            for (int i = 0; i < n; i++) result.Add(new(DiffKind.Removed, start + i + 1, 0, a[start + i]));
            for (int j = 0; j < m; j++) result.Add(new(DiffKind.Added, 0, start + j + 1, b[start + j]));
            return result;
        }

        // Walk back from the end; rows come out reversed.
        int cx = n, cy = m;
        for (int d = found; d > 0; d--)
        {
            var prev = trace[d - 1];
            int k = cx - cy;
            int P(int kk) => prev[kk + d - 1];
            bool down = k == -d || (k != d && P(k - 1) < P(k + 1));
            int pk = down ? k + 1 : k - 1;
            int px = P(pk), py = px - pk;
            int sx = down ? px : px + 1, sy = down ? py + 1 : py;
            while (cx > sx && cy > sy) { cx--; cy--; result.Add(new(DiffKind.Same, start + cx + 1, start + cy + 1, b[start + cy])); }
            if (down) result.Add(new(DiffKind.Added, 0, start + py + 1, b[start + py]));
            else result.Add(new(DiffKind.Removed, start + px + 1, 0, a[start + px]));
            cx = px; cy = py;
        }
        while (cx > 0 && cy > 0) { cx--; cy--; result.Add(new(DiffKind.Same, start + cx + 1, start + cy + 1, b[start + cy])); }
        result.Reverse();
        // Within a change block, show what was removed before what replaced it.
        for (int i = 0; i < result.Count;)
        {
            if (result[i].Kind == DiffKind.Same) { i++; continue; }
            int j = i;
            while (j < result.Count && result[j].Kind != DiffKind.Same) j++;
            var block = result.GetRange(i, j - i);
            result.RemoveRange(i, j - i);
            result.InsertRange(i, block.Where(r => r.Kind == DiffKind.Removed).Concat(block.Where(r => r.Kind == DiffKind.Added)));
            i = j;
        }
        return result;
    }

    /// <summary>Pairs removed and added lines of each block so a replaced line sits next to its replacement.</summary>
    public static List<SidePair> SideBySide(IReadOnlyList<DiffRow> rows)
    {
        var pairs = new List<SidePair>(rows.Count);
        for (int i = 0; i < rows.Count;)
        {
            if (rows[i].Kind == DiffKind.Same) { pairs.Add(new(rows[i], rows[i])); i++; continue; }
            var removed = new List<DiffRow>();
            var added = new List<DiffRow>();
            for (; i < rows.Count && rows[i].Kind != DiffKind.Same; i++)
                (rows[i].Kind == DiffKind.Removed ? removed : added).Add(rows[i]);
            for (int k = 0; k < Math.Max(removed.Count, added.Count); k++)
                pairs.Add(new(k < removed.Count ? removed[k] : null, k < added.Count ? added[k] : null));
        }
        return pairs;
    }

    /// <summary>
    /// Marks per line of the new text (index 0 = line 1): blocks that only add are "added", blocks that replace are
    /// "modified", and a pure deletion marks the boundary on the line below it (or above, at the end of the file).
    /// </summary>
    public static LineMark[] Marks(IReadOnlyList<DiffRow> rows, int newLineCount)
    {
        var marks = new LineMark[newLineCount];
        for (int i = 0; i < rows.Count;)
        {
            if (rows[i].Kind == DiffKind.Same) { i++; continue; }
            bool anyRemoved = false;
            var addedLines = new List<int>();
            for (; i < rows.Count && rows[i].Kind != DiffKind.Same; i++)
            {
                if (rows[i].Kind == DiffKind.Removed) anyRemoved = true;
                else addedLines.Add(rows[i].NewNo);
            }
            if (addedLines.Count > 0)
            {
                foreach (var no in addedLines)
                    if (no >= 1 && no <= newLineCount) marks[no - 1] |= anyRemoved ? LineMark.Modified : LineMark.Added;
                continue;
            }
            // Pure deletion: the next row (if any) is the line just below the gap.
            int below = i < rows.Count ? rows[i].NewNo : 0;
            if (below >= 1 && below <= newLineCount) marks[below - 1] |= LineMark.DeletedAbove;
            else if (newLineCount > 0) marks[newLineCount - 1] |= LineMark.DeletedBelow;
        }
        return marks;
    }

    /// <summary>Every line marked as added: a file that is new to git.</summary>
    public static LineMark[] AllAdded(int lineCount)
    {
        var marks = new LineMark[lineCount];
        Array.Fill(marks, LineMark.Added);
        return marks;
    }

    /// <summary>Line numbers (1-based) where each change block starts, for "next / previous change".</summary>
    public static List<int> ChangeStarts(LineMark[] marks)
    {
        var starts = new List<int>();
        for (int i = 0; i < marks.Length; i++)
        {
            if (marks[i] == LineMark.None) continue;
            bool continues = i > 0 && (marks[i - 1] & (LineMark.Added | LineMark.Modified)) != 0
                                   && (marks[i] & (LineMark.Added | LineMark.Modified)) != 0 && (marks[i] & LineMark.DeletedAbove) == 0;
            if (!continues) starts.Add(i + 1);
        }
        return starts;
    }
}
