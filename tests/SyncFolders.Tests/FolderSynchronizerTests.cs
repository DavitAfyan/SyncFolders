namespace FolderSync.Tests;

public class FolderSynchronizerTests
{
    [Fact]
    public void Copies_files_and_nested_folders()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");
        Tree.WriteFile(temp.At("src/docs/b.txt"), "beta");
        Tree.WriteFile(temp.At("src/docs/deep/c.txt"), "gamma");
        temp.Folder("dst");

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal(Tree.Snapshot(temp.At("src")), Tree.Snapshot(temp.At("dst")));
    }

    [Fact]
    public void Creates_the_replica_folder_when_it_is_missing()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal("alpha", File.ReadAllText(temp.At("dst/a.txt")));
    }

    [Fact]
    public void Empty_folders_are_copied_too()
    {
        using var temp = new TempDir();
        temp.Folder("src/empty/inner");

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.True(Directory.Exists(temp.At("dst/empty/inner")));
    }

    [Fact]
    public void Creation_and_copying_are_logged()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/docs/b.txt"), "beta");
        temp.Folder("dst");
        var log = new TestLog();

        var result = new FolderSynchronizer(temp.At("src"), temp.At("dst"), log).Run();

        string folder = "docs";
        string file = Path.Combine("docs", "b.txt");
        Assert.Equal(new[] { $"INFO Created folder: {folder}", $"INFO Copied file: {file}" }, log.Lines);
        Assert.Equal(1, result.DirectoriesCreated);
        Assert.Equal(1, result.FilesCopied);
    }

    [Fact]
    public void Missing_source_is_an_error()
    {
        using var temp = new TempDir();
        var synchronizer = new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog());

        Assert.Throws<DirectoryNotFoundException>(() => synchronizer.Run());
    }

    [Fact]
    public void Overlapping_folders_are_refused()
    {
        using var temp = new TempDir();
        temp.Folder("src");

        Assert.Throws<ArgumentException>(() => new FolderSynchronizer(temp.At("src"), temp.At("src/replica"), new TestLog()));
        Assert.Throws<ArgumentException>(() => new FolderSynchronizer(temp.At("src"), temp.At("src"), new TestLog()));
    }

    private static readonly DateTime Earlier = new(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Changed_file_is_updated_even_when_the_size_is_the_same()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "aaaa", Earlier);
        Tree.WriteFile(temp.At("dst/a.txt"), "aaaa", Earlier);
        Tree.WriteFile(temp.At("src/a.txt"), "bbbb", Later);
        var log = new TestLog();

        var result = new FolderSynchronizer(temp.At("src"), temp.At("dst"), log).Run();

        Assert.Equal("bbbb", File.ReadAllText(temp.At("dst/a.txt")));
        Assert.Equal(1, result.FilesUpdated);
        Assert.Equal(new[] { "INFO Updated file: a.txt" }, log.Lines);
    }

    [Fact]
    public void Changed_file_with_a_different_size_is_updated()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "short", Earlier);
        Tree.WriteFile(temp.At("dst/a.txt"), "a much longer old version", Earlier);

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal("short", File.ReadAllText(temp.At("dst/a.txt")));
    }

    [Fact]
    public void Unchanged_files_are_left_alone()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha", Earlier);
        var synchronizer = new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog());
        synchronizer.Run();

        var secondLog = new TestLog();
        var second = new FolderSynchronizer(temp.At("src"), temp.At("dst"), secondLog).Run();

        Assert.False(second.HasChanges);
        Assert.Empty(secondLog.Lines);
    }

    [Fact]
    public void File_that_was_only_touched_is_not_copied_again()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha", Earlier);
        Tree.WriteFile(temp.At("dst/a.txt"), "alpha", Later);
        var log = new TestLog();

        var result = new FolderSynchronizer(temp.At("src"), temp.At("dst"), log).Run();

        Assert.False(result.HasChanges);
        Assert.Empty(log.Lines);
        Assert.Equal(Earlier, File.GetLastWriteTimeUtc(temp.At("dst/a.txt")));
    }

    [Fact]
    public void Copied_file_keeps_the_timestamp_of_the_source()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha", Earlier);

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal(Earlier, File.GetLastWriteTimeUtc(temp.At("dst/a.txt")));
    }

    [Fact]
    public void Files_and_folders_missing_from_the_source_are_deleted()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/keep.txt"), "keep");
        Tree.WriteFile(temp.At("dst/keep.txt"), "keep");
        Tree.WriteFile(temp.At("dst/old.txt"), "old");
        Tree.WriteFile(temp.At("dst/old-folder/x.txt"), "x");
        Tree.WriteFile(temp.At("dst/old-folder/sub/y.txt"), "y");
        var log = new TestLog();

        var result = new FolderSynchronizer(temp.At("src"), temp.At("dst"), log).Run();

        Assert.Equal(Tree.Snapshot(temp.At("src")), Tree.Snapshot(temp.At("dst")));
        Assert.Equal(3, result.FilesDeleted);
        Assert.Equal(2, result.DirectoriesDeleted);
        Assert.Contains("INFO Deleted file: old.txt", log.Lines);
        Assert.Contains("INFO Deleted file: " + Path.Combine("old-folder", "sub", "y.txt"), log.Lines);
        Assert.Contains("INFO Deleted folder: old-folder", log.Lines);
    }

    [Fact]
    public void Read_only_files_can_be_updated_and_deleted()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "new", Later);
        Tree.WriteFile(temp.At("dst/a.txt"), "old", Earlier);
        Tree.WriteFile(temp.At("dst/gone.txt"), "x");
        File.SetAttributes(temp.At("dst/gone.txt"), FileAttributes.ReadOnly);

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal(new[] { "a.txt = new" }, Tree.Snapshot(temp.At("dst")));
    }

    [Fact]
    public void Replica_matches_the_source_after_a_mix_of_changes()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/one.txt"), "1", Earlier);
        Tree.WriteFile(temp.At("src/dir/two.txt"), "2", Earlier);
        Tree.WriteFile(temp.At("src/dir/three.txt"), "3", Earlier);
        var synchronizer = new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog());
        synchronizer.Run();

        File.Delete(temp.At("src/one.txt"));
        Tree.WriteFile(temp.At("src/dir/two.txt"), "two, edited", Later);
        Tree.WriteFile(temp.At("src/new/four.txt"), "4", Later);
        Tree.WriteFile(temp.At("dst/stray.txt"), "stray");
        synchronizer.Run();

        Assert.Equal(Tree.Snapshot(temp.At("src")), Tree.Snapshot(temp.At("dst")));
    }
}