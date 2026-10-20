using TextPeach.Services;

namespace TextPeach.Tests;

public class ProjectGroupingTests
{
    private static AppState State(params (string Name, string? Group)[] projects)
    {
        var s = new AppState();
        foreach (var (name, group) in projects)
        {
            s.Projects.Add(new ProjectEntry { Name = name, Path = @"C:\r\" + name, Group = group });
            if (group is not null) ProjectGrouping.Ensure(s, group);
        }
        return s;
    }

    [Fact]
    public void Ensure_is_case_insensitive_and_unique_name_adds_a_suffix()
    {
        var s = new AppState();
        var a = ProjectGrouping.Ensure(s, "Trabajo");
        Assert.Same(a, ProjectGrouping.Ensure(s, "trabajo"));
        Assert.Single(s.Groups);
        Assert.Equal("Trabajo (2)", ProjectGrouping.UniqueName(s, "Trabajo"));
        Assert.Equal("Otro", ProjectGrouping.UniqueName(s, "Otro"));
    }

    [Fact]
    public void Build_lists_groups_in_order_then_ungrouped()
    {
        var s = State(("a", "B-grupo"), ("b", null), ("c", "A-grupo"), ("d", "B-grupo"));
        s.Projects[3].Favorite = true;

        var sections = ProjectGrouping.Build(s, _ => true, hideEmpty: false);

        Assert.Equal(new[] { "B-grupo", "A-grupo", null }, sections.Select(x => x.Group?.Name));
        Assert.Equal(new[] { "d", "a" }, sections[0].Projects.Select(r => r.Name)); // favorites first
        Assert.Equal(new[] { "b" }, sections[2].Projects.Select(r => r.Name));
    }

    [Fact]
    public void Empty_groups_are_hidden_only_when_asked()
    {
        var s = State(("a", null));
        ProjectGrouping.Ensure(s, "Vacío");
        Assert.Contains(ProjectGrouping.Build(s, _ => true, hideEmpty: false), x => x.Group?.Name == "Vacío");
        Assert.DoesNotContain(ProjectGrouping.Build(s, _ => true, hideEmpty: true), x => x.Group?.Name == "Vacío");
    }

    [Fact]
    public void Projects_pointing_to_a_missing_group_count_as_ungrouped()
    {
        var s = State(("a", null));
        s.Projects[0].Group = "ya no existe";
        var section = Assert.Single(ProjectGrouping.Build(s, _ => true, hideEmpty: true));
        Assert.Null(section.Group);
    }

    [Fact]
    public void Rename_moves_members_and_refuses_duplicates()
    {
        var s = State(("a", "Uno"), ("b", "Dos"));
        var uno = ProjectGrouping.Find(s, "Uno")!;

        Assert.False(ProjectGrouping.Rename(s, uno, "dos"));
        Assert.True(ProjectGrouping.Rename(s, uno, "Primero"));
        Assert.Equal("Primero", s.Projects[0].Group);
        Assert.True(ProjectGrouping.Rename(s, uno, "PRIMERO")); // only the case changes
    }

    [Fact]
    public void Delete_ungroups_or_removes_members()
    {
        var s = State(("a", "G"), ("b", "G"), ("c", null));
        s.OpenTabs.Add(@"C:\r\a");

        ProjectGrouping.Delete(s, ProjectGrouping.Find(s, "G")!, removeProjects: false);
        Assert.Empty(s.Groups);
        Assert.All(s.Projects, r => Assert.Null(r.Group));

        var g = ProjectGrouping.Ensure(s, "H");
        ProjectGrouping.Move(s, s.Projects.Take(2).ToList(), g);
        ProjectGrouping.Delete(s, g, removeProjects: true);
        Assert.Equal(new[] { "c" }, s.Projects.Select(r => r.Name));
        Assert.Empty(s.OpenTabs);
    }
}
