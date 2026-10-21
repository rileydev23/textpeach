using TextPeach.Services;

namespace TextPeach.Tests;

public class DiffAndIgnoreTests
{
    [Fact]
    public void DiffKeepsEveryLineOfBothSides()
    {
        // No trailing newline: after one, the editor (and the diff) count an empty last line.
        var rows = LineDiff.Compute("a\nb\nc\nd", "a\nB\nc\nd\ne");
        Assert.Equal(new[] { "a", "b", "c", "d" }, rows.Where(r => r.Kind != DiffKind.Added).Select(r => r.Text));
        Assert.Equal(new[] { "a", "B", "c", "d", "e" }, rows.Where(r => r.Kind != DiffKind.Removed).Select(r => r.Text));
        Assert.Single(rows, r => r.Kind == DiffKind.Removed);
    }

    [Fact]
    public void MarksTellAddedModifiedAndDeleted()
    {
        // b → B (modified), e appended (added), and "x" deleted between c and d.
        var rows = LineDiff.Compute("a\nb\nc\nx\nd\n", "a\nB\nc\nd\ne\n");
        var marks = LineDiff.Marks(rows, 5);
        Assert.Equal(LineMark.None, marks[0]);
        Assert.Equal(LineMark.Modified, marks[1]);
        Assert.Equal(LineMark.None, marks[2]);
        Assert.Equal(LineMark.DeletedAbove, marks[3]);
        Assert.Equal(LineMark.Added, marks[4]);
        Assert.Equal(new[] { 2, 4, 5 }, LineDiff.ChangeStarts(marks));
    }

    [Fact]
    public void DeletionAtTheEndMarksTheLastLine()
    {
        var marks = LineDiff.Marks(LineDiff.Compute("a\nb\nc\n", "a\nb\n"), 2);
        Assert.Equal(LineMark.DeletedBelow, marks[1]);
    }

    [Fact]
    public void SameTextHasNoChanges()
    {
        var rows = LineDiff.Compute("a\r\nb\r\n", "a\r\nb\r\n");
        Assert.All(rows, r => Assert.Equal(DiffKind.Same, r.Kind));
    }

    [Fact]
    public void IgnoreRulesByNamePathAndUnhide()
    {
        var rules = new IgnoreRules(new[] { " dist ", "docs\\old/", "# comment", "", "!node_modules" });
        Assert.Equal(new[] { "dist", "docs/old" }, rules.Lines.Where(l => !l.StartsWith('!')));
        Assert.True(rules.IsIgnoredDir("dist"));
        Assert.True(rules.IsIgnoredDir("web/dist"));
        Assert.True(rules.IsIgnoredDir("docs/old"));
        Assert.False(rules.IsIgnoredDir("old"));
        Assert.False(rules.IsIgnoredDir("node_modules"));
        Assert.True(rules.IsIgnoredDir(".git"));
        Assert.True(rules.IsInsideIgnored("web/dist/app.js"));
        Assert.False(rules.IsInsideIgnored("web/src/app.js"));
    }
}
