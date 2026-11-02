namespace FolderSync.Tests;

public class FileAndConsoleLogTests
{
    [Fact]
    public void Message_goes_to_console_and_file_with_timestamp_and_level()
    {
        using var temp = new TempDir();
        var stdout = new StringWriter();
        string path = temp.At("sync.log");

        using (var log = new FileAndConsoleLog(path, stdout, new StringWriter()))
        {
            log.Info("Copied file: a.txt");
        }

        string fileLine = File.ReadAllLines(path).Single();
        Assert.Equal(fileLine, stdout.ToString().TrimEnd());
        Assert.True(
            System.Text.RegularExpressions.Regex.IsMatch(fileLine, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} \[INFO\] Copied file: a\.txt$"),
            "unexpected line format: " + fileLine);
    }

    [Fact]
    public void Warnings_and_errors_use_stderr()
    {
        using var temp = new TempDir();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        using (var log = new FileAndConsoleLog(temp.At("sync.log"), stdout, stderr))
        {
            log.Info("fine");
            log.Warn("hmm");
            log.Error("bad");
        }

        Assert.DoesNotContain("hmm", stdout.ToString());
        Assert.Contains("[WARN] hmm", stderr.ToString());
        Assert.Contains("[ERROR] bad", stderr.ToString());
        Assert.Equal(3, File.ReadAllLines(temp.At("sync.log")).Length);
    }

    [Fact]
    public void Existing_log_is_appended_to()
    {
        using var temp = new TempDir();
        string path = temp.At("sync.log");

        using (var first = new FileAndConsoleLog(path, new StringWriter(), new StringWriter()))
        {
            first.Info("one");
        }
        using (var second = new FileAndConsoleLog(path, new StringWriter(), new StringWriter()))
        {
            second.Info("two");
        }

        string[] lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("one", lines[0]);
        Assert.Contains("two", lines[1]);
    }

    [Fact]
    public void Missing_log_folder_is_created()
    {
        using var temp = new TempDir();
        string path = temp.At("logs/deeper/sync.log");

        using (var log = new FileAndConsoleLog(path, new StringWriter(), new StringWriter()))
        {
            log.Info("hello");
        }

        Assert.True(File.Exists(path));
    }
}