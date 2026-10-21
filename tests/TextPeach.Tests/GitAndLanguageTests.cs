using System.Text.RegularExpressions;
using TextPeach.Services;

namespace TextPeach.Tests;

public class GitStatusTests
{
    private const string Repo = @"C:\repo";

    [Fact]
    public void ParsesBranchAheadBehindAndFileStates()
    {
        var output = string.Join('\0',
            "## main...origin/main [ahead 2, behind 1]",
            " M src/App.cs",
            "M  README.md",
            "A  docs/new.md",
            "?? notes/",
            "?? todo.txt",
            "R  src/Renamed.cs", "src/Old.cs",
            "UU src/Conflict.cs",
            " D gone.txt",
            "!! bin/") + "\0";
        var s = GitStatus.Parse(output, Repo, Repo);

        Assert.Equal("main", s.Branch);
        Assert.False(s.Detached);
        Assert.Equal(2, s.Ahead);
        Assert.Equal(1, s.Behind);
        Assert.Equal(GitChange.Modified, s.For("src/App.cs", false));
        Assert.Equal(GitChange.Modified, s.For("README.md", false));
        Assert.Equal(GitChange.Added, s.For("docs/new.md", false));
        Assert.Equal(GitChange.Untracked, s.For("todo.txt", false));
        Assert.Equal(GitChange.Untracked, s.For("notes/a/b.txt", false));
        Assert.Equal(GitChange.Renamed, s.For("src/Renamed.cs", false));
        Assert.Equal(GitChange.Conflict, s.For("src/Conflict.cs", false));
        Assert.Equal(GitChange.Deleted, s.For("gone.txt", false));
        Assert.Equal(GitChange.Ignored, s.For("bin/Debug/x.dll", false));
        Assert.Equal(GitChange.None, s.For("src/Clean.cs", false));
        // Folders show the strongest change inside them; ignored content does not count.
        Assert.Equal(GitChange.Conflict, s.For("src", true));
        Assert.Equal(GitChange.Added, s.For("docs", true));
        Assert.Equal(8, s.ChangedCount);
    }

    [Fact]
    public void PathsAreRelativeToAProjectInsideTheRepository()
    {
        var output = "## dev\0 M app/src/a.cs\0 M other/b.cs\0";
        var s = GitStatus.Parse(output, Repo, Path.Combine(Repo, "app"));
        Assert.Equal(GitChange.Modified, s.For("src/a.cs", false));
        Assert.Equal(GitChange.Modified, s.For("src", true));
        Assert.Single(s.Files);
        Assert.Equal("dev", s.Branch);
    }

    [Theory]
    [InlineData("## No commits yet on main", "main", false)]
    [InlineData("## HEAD (no branch)", "HEAD", true)]
    [InlineData("## feature/x", "feature/x", false)]
    public void ParsesSpecialBranchHeaders(string header, string branch, bool detached)
    {
        var s = GitStatus.Parse(header + "\0", Repo, Repo);
        Assert.Equal(branch, s.Branch);
        Assert.Equal(detached, s.Detached);
    }

    [Fact]
    public void FindsTheRepositoryAboveAProjectFolder()
    {
        var root = Directory.CreateTempSubdirectory("tp-git").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            var sub = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;
            Assert.Equal(root, GitStatus.FindRepoRoot(sub));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void GitPeachIsLookedUpNextToTextPeachFirst()
    {
        // Native paths, so it also holds on Linux, where '\' is not a separator.
        var apps = Path.Combine(Path.GetTempPath(), "apps");
        var tools = Path.Combine(Path.GetTempPath(), "tools");
        var sibling = Path.Combine(apps, "GitPeach", "GitPeach.exe");
        var found = GitPeachLocator.Find(p => p == sibling || p == Path.Combine(tools, "GitPeach.exe"),
            Path.Combine(apps, "TextPeach") + Path.DirectorySeparatorChar, null, null, tools);
        Assert.Equal(sibling, found);
    }
}

public class LanguageTests
{
    [Theory]
    [InlineData(null, "es", "es")]
    [InlineData(null, "en", "en")]
    [InlineData(null, "fr", "en")]
    [InlineData("en", "es", "en")]
    [InlineData("es", "en", "es")]
    public void PicksSavedChoiceElseSystemLanguage(string? saved, string system, string expected) =>
        Assert.Equal(expected, Loc.Resolve(saved, system));

    [Fact]
    public void EveryTextExistsInBothLanguagesWithTheSamePlaceholders()
    {
        var placeholder = new Regex(@"\{(\d+)(?::[^}]*)?\}");
        foreach (var (key, (en, es)) in Strings.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.False(string.IsNullOrWhiteSpace(es), key);
            var a = placeholder.Matches(en).Select(m => m.Groups[1].Value).Distinct().Order();
            var b = placeholder.Matches(es).Select(m => m.Groups[1].Value).Distinct().Order();
            Assert.True(a.SequenceEqual(b), key);
        }
    }

    [Fact]
    public void EveryKeyUsedInTheSourceExists()
    {
        var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/TextPeach"));
        var used = new Regex(@"Loc\.(?:T|F)\(""([\w.]+)""|Loc\.Plural\(""([\w.]+)""|\{l:L ([\w.]+)\}");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".axaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in used.Matches(File.ReadAllText(file)))
            {
                if (m.Groups[1].Success && !Strings.All.ContainsKey(m.Groups[1].Value)) missing.Add(m.Groups[1].Value);
                if (m.Groups[2].Success && !(Strings.All.ContainsKey(m.Groups[2].Value + ".one") && Strings.All.ContainsKey(m.Groups[2].Value + ".many")))
                    missing.Add(m.Groups[2].Value + ".one/.many");
                if (m.Groups[3].Success && !Strings.All.ContainsKey(m.Groups[3].Value)) missing.Add(m.Groups[3].Value);
            }
        }
        Assert.Empty(missing);
    }
}
