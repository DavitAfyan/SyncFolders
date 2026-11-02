namespace FolderSync.Tests;

public class SyncLoopTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Keeps_the_replica_up_to_date_until_cancelled()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");
        var log = new TestLog();
        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => SyncLoop.RunAsync(
            new FolderSynchronizer(temp.At("src"), temp.At("dst"), log), Fast, log, cts.Token));

        Assert.True(await WaitUntil(() => File.Exists(temp.At("dst/a.txt"))), "first file was not copied");

        Tree.WriteFile(temp.At("src/b.txt"), "beta");
        File.Delete(temp.At("src/a.txt"));

        Assert.True(await WaitUntil(() => File.Exists(temp.At("dst/b.txt")) && !File.Exists(temp.At("dst/a.txt"))),
            "later changes were not picked up");

        cts.Cancel();
        await loop;
    }

    [Fact]
    public async Task Stops_promptly_when_cancelled_during_the_wait()
    {
        using var temp = new TempDir();
        temp.Folder("src");
        var log = new TestLog();
        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => SyncLoop.RunAsync(
            new FolderSynchronizer(temp.At("src"), temp.At("dst"), log), TimeSpan.FromHours(1), log, cts.Token));
        Assert.True(await WaitUntil(() => Directory.Exists(temp.At("dst"))));

        cts.Cancel();

        Assert.True(await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(5))) == loop, "loop did not stop");
    }

    [Fact]
    public async Task A_failed_pass_is_logged_and_the_next_one_still_runs()
    {
        using var temp = new TempDir();
        var log = new TestLog();
        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => SyncLoop.RunAsync(
            new FolderSynchronizer(temp.At("src"), temp.At("dst"), log), Fast, log, cts.Token));

        // The source doesn't exist yet so the first passes fail
        Assert.True(await WaitUntil(() => log.Lines.Any(l => l.StartsWith("ERROR"))), "no error was logged");
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");

        Assert.True(await WaitUntil(() => File.Exists(temp.At("dst/a.txt"))), "loop did not recover");

        cts.Cancel();
        await loop;
    }

    [Fact]
    public async Task Idle_passes_do_not_clutter_the_log()
    {
        using var temp = new TempDir();
        Tree.WriteFile(temp.At("src/a.txt"), "alpha");
        var log = new TestLog();
        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => SyncLoop.RunAsync(
            new FolderSynchronizer(temp.At("src"), temp.At("dst"), log), Fast, log, cts.Token));
        Assert.True(await WaitUntil(() => File.Exists(temp.At("dst/a.txt"))));

        await Task.Delay(TimeSpan.FromMilliseconds(400)); // several idle passes
        cts.Cancel();
        await loop;

        Assert.Equal(1, log.Lines.Count(l => l.Contains("Synchronization finished")));
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(20);
        }
        return condition();
    }
}