using TextPeach.Services;

namespace TextPeach.Tests;

public sealed class FileOpsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textpeach-fileops-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Touch(string rel, string text = "")
    {
        var full = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    [Fact]
    public void FreeNameCountsCopiesLikeTheExplorer()
    {
        Assert.Equal("a.txt", FileOps.FreeName(_dir, "a.txt", " - copy"));
        Touch("a.txt");
        Assert.Equal("a - copy.txt", FileOps.FreeName(_dir, "a.txt", " - copy"));
        Touch("a - copy.txt");
        Assert.Equal("a - copy (2).txt", FileOps.FreeName(_dir, "a.txt", " - copy"));
        // A dotfile has no stem: the copy goes after the whole name.
        Touch(".gitignore");
        Assert.Equal(".gitignore - copy", FileOps.FreeName(_dir, ".gitignore", " - copy"));
        // A folder keeps its dots ("v1.2" is a name, not an extension).
        Directory.CreateDirectory(Path.Combine(_dir, "v1.2"));
        Assert.Equal("v1.2 - copy", FileOps.FreeName(_dir, "v1.2", " - copy", isDirectory: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("what?")]
    [InlineData("trailing.")]
    public void RejectsNamesNoSystemAccepts(string name) => Assert.NotNull(FileOps.InvalidNameReason(name));

    [Fact]
    public void CreateFileMakesTheMissingFolders()
    {
        var full = FileOps.CreateFile(_dir, "src/util/a.cs");
        Assert.True(File.Exists(full));
        Assert.Equal(Path.Combine(_dir, "src", "util", "a.cs"), full);
        Assert.Throws<IOException>(() => FileOps.CreateFile(_dir, "src/util/a.cs"));
        Assert.Throws<IOException>(() => FileOps.CreateFolder(_dir, "src/../x"));
    }

    [Fact]
    public void RenameChangesOnlyTheCase()
    {
        var full = Touch("readme.md", "hi");
        var renamed = FileOps.Rename(full, "README.md");
        Assert.Equal("README.md", Path.GetFileName(renamed));
        Assert.Equal("README.md", Path.GetFileName(Directory.GetFiles(_dir).Single()));
        Assert.Equal("hi", File.ReadAllText(renamed));
    }

    [Fact]
    public void RenameNeverOverwrites()
    {
        var a = Touch("a.txt", "a");
        Touch("b.txt", "b");
        Assert.Throws<IOException>(() => FileOps.Rename(a, "b.txt"));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_dir, "b.txt")));
    }

    [Fact]
    public void CopyIntoCopiesFoldersWholeAndBesideItself()
    {
        Touch("src/a.cs", "a");
        Touch("src/sub/b.cs", "b");
        var copy = FileOps.CopyInto(Path.Combine(_dir, "src"), _dir, " - copy");
        Assert.Equal(Path.Combine(_dir, "src - copy"), copy);
        Assert.Equal("b", File.ReadAllText(Path.Combine(copy, "sub", "b.cs")));
        Assert.Throws<IOException>(() => FileOps.CopyInto(Path.Combine(_dir, "src"), Path.Combine(_dir, "src", "sub"), " - copy"));
    }

    [Fact]
    public void MoveIntoRefusesItselfAndTakenNames()
    {
        var src = Path.Combine(_dir, "src");
        Touch("src/a.cs");
        Touch("dest/a.cs");
        Assert.Throws<IOException>(() => FileOps.MoveInto(src, Path.Combine(src, "inner")));
        Assert.Throws<IOException>(() => FileOps.MoveInto(Path.Combine(src, "a.cs"), Path.Combine(_dir, "dest")));
        // Already there: nothing to do.
        Assert.Equal(Path.Combine(src, "a.cs"), FileOps.MoveInto(Path.Combine(src, "a.cs"), src));
        var moved = FileOps.MoveInto(src, Path.Combine(_dir, "dest"));
        Assert.True(File.Exists(Path.Combine(moved, "a.cs")));
        Assert.False(Directory.Exists(src));
    }

    [Fact]
    public void InsideMeansBelowNotJustAPrefix()
    {
        Assert.True(FileOps.IsSameOrInside(Path.Combine(_dir, "src", "a.cs"), Path.Combine(_dir, "src")));
        Assert.True(FileOps.IsSameOrInside(Path.Combine(_dir, "src"), Path.Combine(_dir, "src")));
        Assert.False(FileOps.IsSameOrInside(Path.Combine(_dir, "src2", "a.cs"), Path.Combine(_dir, "src")));
    }
}
