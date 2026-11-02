using Xunit.Sdk;

namespace FolderSync.Tests;

public class OptionsValidationTests
{
    private static SyncOptions Parse(string source, string replica, string log) =>
        OptionsParser.Parse(new[] { "--source", source, "--replica", replica, "--interval", "5", "--log", log });

    [Fact]
    public void Relative_paths_become_absolute()
    {
        using var temp = new TempDir();
        temp.Folder("src");
        string oldCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(temp.Path);
        try
        {
            var options = Parse("src", "dst", "sync.log");

            Assert.Equal(temp.At("src"), options.SourcePath);
            Assert.Equal(temp.At("dst"), options.ReplicaPath);
            Assert.Equal(temp.At("sync.log"), options.LogPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(oldCwd);
        }
    }

    [Fact]
    public void Trailing_separator_is_dropped()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");

        var options = Parse(source + Path.DirectorySeparatorChar, temp.At("dst") + Path.DirectorySeparatorChar, temp.At("l.log"));

        Assert.Equal(source, options.SourcePath);
        Assert.Equal(temp.At("dst"), options.ReplicaPath);
    }

    [Fact]
    public void Missing_source_folder_is_rejected()
    {
        using var temp = new TempDir();

        var ex = Assert.Throws<UsageException>(() => Parse(temp.At("nope"), temp.At("dst"), temp.At("l.log")));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Replica_does_not_have_to_exist()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");

        var options = Parse(source, temp.At("not-yet"), temp.At("l.log"));

        Assert.Equal(temp.At("not-yet"), options.ReplicaPath);
    }

    [Fact]
    public void Replica_equal_to_source_is_rejected()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");

        Assert.Throws<UsageException>(() => Parse(source, source, temp.At("l.log")));
    }

    [Fact]
    public void Replica_inside_source_is_rejected()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");

        Assert.Throws<UsageException>(() => Parse(source, Path.Combine(source, "replica"), temp.At("l.log")));
    }

    [Fact]
    public void Source_inside_replica_is_rejected()
    {
        using var temp = new TempDir();
        string replica = temp.Folder("dst");
        string source = temp.Folder("dst/src");

        Assert.Throws<UsageException>(() => Parse(source, replica, temp.At("l.log")));
    }

    [Fact]
    public void Folders_that_only_share_a_name_prefix_are_fine()
    {
        using var temp = new TempDir();
        string source = temp.Folder("data");
        string replica = temp.At("data-copy");

        var options = Parse(source, replica, temp.At("l.log"));

        Assert.Equal(replica, options.ReplicaPath);
    }

    [Fact]
    public void Log_inside_replica_is_rejected()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");
        string replica = temp.Folder("dst");

        var ex = Assert.Throws<UsageException>(() => Parse(source, replica, Path.Combine(replica, "sync.log")));

        Assert.Contains("replica", ex.Message);
    }

    [Fact]
    public void Log_inside_source_is_rejected()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");

        var ex = Assert.Throws<UsageException>(() => Parse(source, temp.At("dst"), Path.Combine(source, "sync.log")));

        Assert.Contains("source", ex.Message);
    }

    [Fact]
    public void Log_path_pointing_at_a_folder_is_rejected()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");
        string folder = temp.Folder("logs");

        Assert.Throws<UsageException>(() => Parse(source, temp.At("dst"), folder));
    }
}