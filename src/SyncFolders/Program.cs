using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace FolderSync;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // check for help flags first so we don't do unnecessary work
        // TODO: Check if '-?' is actually valid in linux
        bool wantsHelp = false;
        foreach (var arg in args)
        {
            if (arg == "-h" || arg == "--help" || arg == "-?")
            {
                wantsHelp = true;
                break;
            }
        }

        if (wantsHelp)
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
            // printing errors to stderr
            Console.Error.WriteLine("Error: " + ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(OptionsParser.Usage);
            return 2; // exit code 2 for bad args
        }

        FileAndConsoleLog log = null; // had to pull this out of the using statement to handle init errors nicely
        try
        {
            log = new FileAndConsoleLog(options.LogPath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: cannot open the log file '{options.LogPath}': {ex.Message}");
            return 2;
        }

        using (log)
        using (var cts = new CancellationTokenSource())
        {
            // Ctrl+C and SIGTERM (docker stop, systemd) both end the loop after the current file
            // Note: I remember running into issues with docker stop not triggering this properly, 
            // but the PosixSignalRegistration below seems to fix it.
            ConsoleCancelEventHandler onCancelKey = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += onCancelKey;

            PosixSignalRegistration onTerminate = null;
            try
            {
                onTerminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                {
                    context.Cancel = true;
                    cts.Cancel();
                });
            }
            catch
            {
                // sometimes this throws on Windows, so just catch and ignore
            }

            try
            {
                log.Info("Synchronizing '" + options.SourcePath + "' to '" + options.ReplicaPath + "' every " + options.Interval.TotalSeconds + " s. Press Ctrl+C to stop.");

                var synchronizer = new FolderSynchronizer(options.SourcePath, options.ReplicaPath, log);
                await SyncLoop.RunAsync(synchronizer, options.Interval, log, cts.Token);

                log.Info("Stopped.");

                // clean up the posix thing if it was created
                onTerminate?.Dispose();

                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= onCancelKey;
                onTerminate?.Dispose();
            }
        }
    }
}