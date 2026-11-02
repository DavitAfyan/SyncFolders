using System.Runtime.InteropServices;

namespace FolderSync;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (OptionsParser.WantsHelp(args))
        {
            Console.WriteLine(OptionsParser.Usage);
            return 0;
        }

        SyncOptions options;
        try
        {
            options = OptionsParser.Parse(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(OptionsParser.Usage);
            return 2;
        }

        FileAndConsoleLog log;
        try
        {
            log = new FileAndConsoleLog(options.LogPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: cannot open the log file '{options.LogPath}': {ex.Message}");
            return 2;
        }

        using (log)
        using (var cts = new CancellationTokenSource())
        {
            // Ctrl+C and SIGTERM both end the loop after the current file
            ConsoleCancelEventHandler onCancelKey = (_, e) =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += onCancelKey;
            using PosixSignalRegistration onTerminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                context.Cancel = true;
                cts.Cancel();
            });

            try
            {
                log.Info($"Synchronizing '{options.SourcePath}' to '{options.ReplicaPath}' every {options.Interval.TotalSeconds:0} s. Press Ctrl+C to stop.");

                var synchronizer = new FolderSynchronizer(options.SourcePath, options.ReplicaPath, log);
                await SyncLoop.RunAsync(synchronizer, options.Interval, log, cts.Token);

                log.Info("Stopped.");
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= onCancelKey;
            }
        }
    }
}

