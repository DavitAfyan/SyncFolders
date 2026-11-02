using System.Security.Cryptography;

namespace FolderSync;

// The main program: One-way syncronizer
// Symbolic links are not followed and not copied
public sealed class FolderSynchronizer
{
    private readonly string _sourcePath;
    private readonly string _replicaPath;
    private readonly ILog _log;
    private readonly HashSet<string> _reported = new(PathUtil.NameComparer);

    public FolderSynchronizer(string sourcePath, string replicaPath, ILog log)
    {
        // quick sanity check so we don't accidentally sync a folder into itself
        if (PathUtil.IsSameOrInside(replicaPath, sourcePath) || PathUtil.IsSameOrInside(sourcePath, replicaPath))
        {
            throw new ArgumentException("Source and replica folders must not be the same or contain one another.");
        }

        _sourcePath = Path.GetFullPath(sourcePath);
        _replicaPath = Path.GetFullPath(replicaPath);
        _log = log;
    }

    // Runs a sync pass. Returns stats about what happened. 
    public SyncResult Run(CancellationToken cancellationToken = default)
    {
        var result = new SyncResult();

        var source = new DirectoryInfo(_sourcePath);
        if (!source.Exists)
        {
            // throw here otherwise it wipes the replica
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
        // read the source first. If that fails we bail out before deleting anything from the replica
        Dictionary<string, FileSystemInfo> sourceEntries = ReadSourceEntries(source, relativePath);

        // find stuff in replica that's no longer in source
        var extras = replica.EnumerateFileSystemInfos().Where(e => !MatchesSource(e, sourceEntries)).ToList();
        foreach (FileSystemInfo extra in extras)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = Path.Combine(relativePath, extra.Name);
            Attempt(relative, result, () => Delete(extra, relative, result));
        }

        foreach (FileSystemInfo entry in sourceEntries.Values.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relative = Path.Combine(relativePath, entry.Name);
            string target = Path.Combine(replica.FullName, entry.Name);

            Attempt(relative, result, () =>
            {
                // check if folder or a file
                if (entry is DirectoryInfo directory)
                {
                    SyncSubdirectory(directory, target, relative, result, cancellationToken);
                }
                else if (entry is FileInfo file)
                {
                    SyncFile(file, target, relative, result);
                }
            });
        }
    }

    private void SyncSubdirectory(DirectoryInfo source, string targetPath, string relative, SyncResult result, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(targetPath))
        {
            Directory.CreateDirectory(targetPath);
            _log.Info($"Created folder: {relative}");
            result.DirectoriesCreated++;
        }

        SyncDirectory(source, new DirectoryInfo(targetPath), relative, result, cancellationToken);
    }

    private void SyncFile(FileInfo source, string targetPath, string relative, SyncResult result)
    {
        var target = new FileInfo(targetPath);
        if (!target.Exists)
        {
            CopyFile(source, targetPath);
            _log.Info($"Copied file: {relative}");
            result.FilesCopied++;
        }
        else if (!HasSameContent(source, target))
        {
            CopyFile(source, targetPath);
            _log.Info($"Updated file: {relative}");
            result.FilesUpdated++;
        }
    }

    private Dictionary<string, FileSystemInfo> ReadSourceEntries(DirectoryInfo source, string relativePath)
    {
        var entries = new Dictionary<string, FileSystemInfo>(PathUtil.NameComparer);

        foreach (FileSystemInfo entry in source.EnumerateFileSystemInfos())
        {
            string relative = Path.Combine(relativePath, entry.Name);

            if (IsLink(entry))
            {
                ReportOnce(relative, $"Skipping symbolic link: {relative}");
            }
            else if (!entries.TryAdd(entry.Name, entry))
            {
                // Only possible on a case sensitive source
                ReportOnce(relative, $"Skipping {relative}: another entry in the same folder differs from it only by letter case");
            }
        }

        return entries;
    }


    // A replica entry stays only if the source has an entry with the same name and the same kind
    // Anything else has to be removed first
    private static bool MatchesSource(FileSystemInfo replicaEntry, Dictionary<string, FileSystemInfo> sourceEntries)
    {
        return !IsLink(replicaEntry)
            && sourceEntries.TryGetValue(replicaEntry.Name, out FileSystemInfo? sourceEntry)
            && (sourceEntry is DirectoryInfo) == (replicaEntry is DirectoryInfo)
            // The replica should use the source spelling for CaseInSensitive OS
            && sourceEntry.Name.Equals(replicaEntry.Name, StringComparison.Ordinal);

    }

    private void Delete(FileSystemInfo entry, string relative, SyncResult result)
    {
        ClearReadOnly(entry);

        if (entry is DirectoryInfo directory && !IsLink(entry))
        {
            // recursion for subdirs
            foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos().ToList())
            {
                Delete(child, Path.Combine(relative, child.Name), result);
            }
            directory.Delete();
            _log.Info($"Deleted folder: {relative}");
            result.DirectoriesDeleted++;
        }
        else
        {
            // For a link this removes the link itself (NOT what it points to)
            entry.Delete();
            _log.Info($"Deleted file: {relative}");
            result.FilesDeleted++;
        }
    }


    // Copies to a temp file first so we don't mess up the target if it crashes midway
    // If the process dies halfway the leftover temp file is deleted 
    private static void CopyFile(FileInfo source, string targetPath)
    {
        string tempPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $".foldersync-{Guid.NewGuid():N}.tmp");

        try
        {
            source.CopyTo(tempPath);
            // keep old timestamp so sync logic doesn't think it changed next time
            File.SetLastWriteTimeUtc(tempPath, source.LastWriteTimeUtc);

            var existing = new FileInfo(targetPath);
            if (existing.Exists)
            {
                ClearReadOnly(existing);
            }
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }



    private static bool HasSameContent(FileInfo source, FileInfo target)
    {
        // size check is super fast, do that first 
        if (source.Length != target.Length)
        {
            return false;
        }

        if (source.LastWriteTimeUtc == target.LastWriteTimeUtc)
        {
            return true;
        }

        // sizes match and timestamps don't so we have to hash it 
        if (!ComputeHash(source).SequenceEqual(ComputeHash(target)))
        {
            return false;
        }

        // Same content so fix the timestamp so we skip hashing next time
        File.SetLastWriteTimeUtc(target.FullName, source.LastWriteTimeUtc);
        return true;
    }

    private static byte[] ComputeHash(FileInfo file)
    {
        using FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return MD5.HashData(stream);
    }

    // an I/O or permission problem is logged and doesn't crash the pass
    private void Attempt(string relative, SyncResult result, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Could not synchronize '{relative}': {ex.Message}");
            result.Errors++;
        }
    }

    private void ReportOnce(string relative, string message)
    {
        if (_reported.Add(relative))
        {
            _log.Warn(message);
        }
    }

    private static bool IsLink(FileSystemInfo entry)
    {
        return entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && entry.LinkTarget != null;
    }

    private static void ClearReadOnly(FileSystemInfo entry)
    {
        if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            entry.Attributes &= ~FileAttributes.ReadOnly;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done
            // the next pass removes the stray file
        }
    }
}