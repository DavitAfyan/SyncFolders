namespace FolderSync;

public sealed class SyncResult
{
    public int DirectoriesCreated { get; internal set; }

    public int FilesCopied { get; internal set; }

    public int Errors { get; internal set; }

    public bool HasChanges => DirectoriesCreated + FilesCopied > 0;
}