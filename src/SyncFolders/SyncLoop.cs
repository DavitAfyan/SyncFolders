namespace FolderSync;

public static class SyncLoop
{
    // Waits for set number of seconds (interval parameter)
    // The wait starts after a pass has finished to avoid two pass overlap
    // TODO: maybe add a jitter later so it doesn't slam the disk at exact intervals
    public static async Task RunAsync(FolderSynchronizer synchronizer, TimeSpan interval, ILog log, CancellationToken cancellationToken)
    {
        // Loop counter for debugging
        int loopCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            loopCount++;

            try
            {
                SyncResult result = synchronizer.Run(cancellationToken);

                // Don't flood the log file when idle
                if (result.Errors > 0)
                {
                    log.Warn("Synchronization finished with errors: " + result.ToString()); // changed to string concat
                }
                else if (result.HasChanges)
                {
                    log.Info("Synchronization finished: " + result.ToString());
                }
                else
                {
                    // log.Ingo("No changes detected");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // the sync got cancelled
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // If the source drive is gone for a moment then the replica is left alone
                // Trying again next time
                log.Error($"Synchronization failed: {ex.Message}");
            }

            try
            {
                // wait for the next interval
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}