namespace FolderSync;

// The main program: One-way syncronizer
// TODO: Removing file, changing file
public sealed class FolderSynchronizer
{
    private readonly string _sourcePath;
    private readonly string _replicaPath;
    private readonly ILog _log;

    public FolderSynchronizer(string sourcePath, string replicaPath, ILog log)
    {
        if (PathUtil.IsSameOrInside(replicaPath, sourcePath) || PathUtil.IsSameOrInside(sourcePath, replicaPath))
        {
            throw new ArgumentException("Source and replica folders must not be the same or contain one another.");
        }

        _sourcePath = Path.GetFullPath(sourcePath);
        _replicaPath = Path.GetFullPath(replicaPath);
        _log = log;
    }

    public SyncResult Run(CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();

        var source = new DirectoryInfo(_sourcePath);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Source folder '{_sourcePath}' does not exist.");
        }

        // Replica folder is created if doens't exist
        var replica = new DirectoryInfo(_replicaPath);
        if (!replica.Exists)
        {
            replica.Create();
            _log.Info("Created folder: " + _replicaPath);
            result.DirectoriesCreated++;
        }

        SyncDirectory(source, replica, string.Empty, result, cancellationToken);
        return result;
    }

    private void SyncDirectory(DirectoryInfo source, DirectoryInfo replica, string relativePath, SyncResult result, CancellationToken cancellationToken)
    {
        foreach (FileSystemInfo entry in source.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = Path.Combine(relativePath, entry.Name);
            string target = Path.Combine(replica.FullName, entry.Name);

            if (entry is DirectoryInfo directory)
            {
                if (!Directory.Exists(target))
                {
                    Directory.CreateDirectory(target);
                    _log.Info($"Created folder: {relative}");
                    result.DirectoriesCreated = result.DirectoriesCreated + 1;
                }
                // recurse down into subdirectories
                SyncDirectory(directory, new DirectoryInfo(target), relative, result, cancellationToken);
            }
            else if (entry is FileInfo file && !File.Exists(target))
            {
                file.CopyTo(target);
                _log.Info($"Copied file: {relative}");
                result.FilesCopied++;
            }
        }
    }
}