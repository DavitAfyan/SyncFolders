namespace FolderSync;

// Command line arguments
public sealed record SyncOptions(
    string SourcePath,
    string ReplicaPath,
    TimeSpan Interval,
    string LogPath);