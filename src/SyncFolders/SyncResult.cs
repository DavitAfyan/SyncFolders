namespace FolderSync;

public sealed class SyncResult
{
    public int DirectoriesCreated { get; internal set; }

    public int FilesCopied { get; internal set; }

    public int FilesUpdated { get; internal set; }

    public int FilesDeleted { get; internal set; }

    public int DirectoriesDeleted { get; internal set; }

    public int Errors { get; internal set; }

    public bool HasChanges =>
        DirectoriesCreated + FilesCopied + FilesUpdated + FilesDeleted + DirectoriesDeleted > 0;

    public override string ToString() =>
        $"{DirectoriesCreated} folders created, {FilesCopied} files copied, {FilesUpdated} files updated, " +
        $"{FilesDeleted} files deleted, {DirectoriesDeleted} folders deleted, {Errors} errors";
}