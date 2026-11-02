namespace FolderSync.Tests;

// Small helpers for folder tree building and comparison
public static class Tree
{
    public static void WriteFile(string path, string content, DateTime? lastWriteUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (lastWriteUtc != null)
        {
            File.SetLastWriteTimeUtc(path, lastWriteUtc.Value);
        }
    }


    // Two folders with the same snapshot are identical as far as the sync is concerned
    public static string[] Snapshot(string root)
    {
        return Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                return Directory.Exists(path) ? relative + "/" : relative + " = " + File.ReadAllText(path);
            })
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
    }
}