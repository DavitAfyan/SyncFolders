namespace FolderSync.Tests;

public class FolderSynchronizerEdgeCaseTests
{
    [Fact]
    public void File_in_replica_is_replaced_by_a_folder_from_the_source()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/thing/inside.txt"), "inside");
        Tree.WriteFile(temp.At("dst/thing"), "i used to be a file");

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal(Tree.Snapshot(temp.At("src")), Tree.Snapshot(temp.At("dst")));
    }

    [Fact]
    public void Folder_in_replica_is_replaced_by_a_file_from_the_source()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/thing"), "now i am a file");
        Tree.WriteFile(temp.At("dst/thing/inside.txt"), "inside");

        var result = new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal(Tree.Snapshot(temp.At("src")), Tree.Snapshot(temp.At("dst")));
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public void Leftover_temporary_files_are_cleaned_up()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");
        Tree.WriteFile(temp.At("dst/.foldersync-0123456789abcdef.tmp"), "half a file");

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal(new[] { "a.txt = alpha" }, Tree.Snapshot(temp.At("dst")));
    }

    [Fact]
    public void Source_that_disappears_does_not_wipe_the_replica()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");
        var synchronizer = new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog());
        synchronizer.Run();

        Directory.Delete(temp.At("src"), recursive: true);

        Assert.Throws<DirectoryNotFoundException>(() => synchronizer.Run());
        Assert.Equal(new[] { "a.txt = alpha" }, Tree.Snapshot(temp.At("dst")));
    }

    [Fact]
    public void Cancelled_token_stops_the_pass()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var synchronizer = new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog());

        Assert.Throws<OperationCanceledException>(() => synchronizer.Run(cts.Token));
    }

    [Fact]
    public void Symbolic_links_in_the_source_are_skipped_and_reported_once()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("outside/secret.txt"), "not part of the source");
        Tree.WriteFile(temp.At("src/real.txt"), "real");
        if (!TryCreateSymlink(temp.At("src/link-to-folder"), temp.At("outside"), isFolder: true)
            || !TryCreateSymlink(temp.At("src/link-to-file"), temp.At("outside/secret.txt"), isFolder: false))
        {
            return; // creating symlinks isn't allowed here (Windows without developer mode)
        }
        var log = new TestLog();
        var synchronizer = new FolderSynchronizer(temp.At("src"), temp.At("dst"), log);

        synchronizer.Run();
        synchronizer.Run();

        Assert.Equal(new[] { "real.txt = real" }, Tree.Snapshot(temp.At("dst")));
        Assert.Equal(1, log.Lines.Count(l => l.Contains("link-to-folder")));
        Assert.Equal(1, log.Lines.Count(l => l.Contains("link-to-file")));
    }

    [Fact]
    public void Symbolic_link_in_the_replica_is_removed_without_touching_its_target()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("outside/precious.txt"), "do not delete me");
        Tree.WriteFile(temp.At("src/real.txt"), "real");
        temp.Folder("dst");
        if (!TryCreateSymlink(temp.At("dst/sneaky"), temp.At("outside"), isFolder: true))
        {
            return;
        }

        new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog()).Run();

        Assert.Equal(new[] { "real.txt = real" }, Tree.Snapshot(temp.At("dst")));
        Assert.Equal("do not delete me", File.ReadAllText(temp.At("outside/precious.txt")));
    }

    [Fact]
    public void One_unreadable_file_does_not_stop_the_rest()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a-locked.txt"), "secret");
        Tree.WriteFile(temp.At("src/b-fine.txt"), "fine");
        File.SetUnixFileMode(temp.At("src/a-locked.txt"), UnixFileMode.None);
        if (CanRead(temp.At("src/a-locked.txt")))
        {
            return; // running as root, permissions don't apply
        }
        var log = new TestLog();

        var result = new FolderSynchronizer(temp.At("src"), temp.At("dst"), log).Run();

        Assert.Equal(1, result.Errors);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal("fine", File.ReadAllText(temp.At("dst/b-fine.txt")));
        Assert.Contains(log.Lines, l => l.StartsWith("ERROR") && l.Contains("a-locked.txt"));
        // and no temp file is left behind
        Assert.Equal(new[] { "b-fine.txt = fine" }, Tree.Snapshot(temp.At("dst")));
    }

    [Fact]
    public void Unreadable_source_folder_keeps_its_replica_counterpart()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/locked/a.txt"), "alpha");
        Tree.WriteFile(temp.At("src/other.txt"), "other");
        var synchronizer = new FolderSynchronizer(temp.At("src"), temp.At("dst"), new TestLog());
        synchronizer.Run();

        File.SetUnixFileMode(temp.At("src/locked"), UnixFileMode.None);
        try
        {
            if (CanList(temp.At("src/locked")))
            {
                return; // running as root
            }

            var result = synchronizer.Run();

            Assert.Equal(1, result.Errors);
            Assert.Equal("alpha", File.ReadAllText(temp.At("dst/locked/a.txt")));
        }
        finally
        {
            File.SetUnixFileMode(temp.At("src/locked"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool TryCreateSymlink(string linkPath, string target, bool isFolder)
    {
        try
        {
            if (isFolder)
            {
                Directory.CreateSymbolicLink(linkPath, target);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, target);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanList(string path)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Any();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}