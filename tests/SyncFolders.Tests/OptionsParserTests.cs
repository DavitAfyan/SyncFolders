using Xunit.Sdk;

namespace FolderSync.Tests;

public class OptionsParserTests
{
    [Fact]
    public void Parses_all_options()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");

        var options = OptionsParser.Parse(new[]
        {
            "--source", source, "--replica", temp.At("dst"), "--interval", "30", "--log", temp.At("sync.log")
        });

        Assert.Equal(source, options.SourcePath);
        Assert.Equal(temp.At("dst"), options.ReplicaPath);
        Assert.Equal(temp.At("sync.log"), options.LogPath);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Interval);
    }

    [Fact]
    public void Accepts_equals_syntax_and_any_order()
    {
        using var temp = new TempDir();
        string source = temp.Folder("src");

        var options = OptionsParser.Parse(new[]
        {
            "--log=" + temp.At("sync.log"), "--interval=5", "--replica=" + temp.At("dst"), "--source=" + source
        });

        Assert.Equal(source, options.SourcePath);
        Assert.Equal(temp.At("dst"), options.ReplicaPath);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Interval);
    }

    [Fact]
    public void Missing_option_is_reported_by_name()
    {
        var ex = Assert.Throws<UsageException>(() =>
            OptionsParser.Parse(new[] { "--source", "a", "--replica", "b", "--interval", "5" }));

        Assert.Contains("--log", ex.Message);
    }

    [Fact]
    public void Option_without_a_value_is_rejected()
    {
        var ex = Assert.Throws<UsageException>(() =>
            OptionsParser.Parse(new[] { "--source", "a", "--replica", "b", "--log", "l", "--interval" }));

        Assert.Contains("--interval", ex.Message);
    }

    [Fact]
    public void Unknown_option_is_rejected()
    {
        var ex = Assert.Throws<UsageException>(() => OptionsParser.Parse(new[] { "--sauce", "a" }));

        Assert.Contains("--sauce", ex.Message);
    }

    [Fact]
    public void Stray_positional_argument_is_rejected()
    {
        Assert.Throws<UsageException>(() => OptionsParser.Parse(new[] { "src", "dst" }));
    }

    [Fact]
    public void Repeated_option_is_rejected()
    {
        var ex = Assert.Throws<UsageException>(() => OptionsParser.Parse(new[]
        {
            "--source", "a", "--source", "b", "--replica", "c", "--interval", "5", "--log", "l"
        }));

        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public void Empty_value_is_rejected()
    {
        Assert.Throws<UsageException>(() => OptionsParser.Parse(new[]
        {
            "--source", "", "--replica", "b", "--interval", "5", "--log", "l"
        }));
    }

    [Fact]
    public void Interval_must_be_a_positive_whole_number()
    {
        foreach (string bad in new[] { "0", "-3", "abc", "1.5", "" })
        {
            Assert.Throws<UsageException>(() => OptionsParser.Parse(new[]
            {
                "--source", "a", "--replica", "b", "--interval", bad, "--log", "l"
            }));
        }
    }
}