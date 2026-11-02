namespace FolderSync;

internal static class PathUtil
{
    // Links are followed at most this many times so a link loop can't hang the program
    private const int MaxLinkDepth = 32;

    // Linux is not case sencitive
    private static readonly bool CaseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparison Comparison =>
        CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer NameComparer =>
        CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // True if path is the folder itself or lies somewhere below it
    public static bool IsSameOrInside(string path, string folder)
    {
        string fullPath = Canonicalize(path);
        string fullFolder = Canonicalize(folder);

        if (fullPath.Equals(fullFolder, Comparison))
        {
            return true;
        }

        // Compare with a trailing separator
        string prefix = fullFolder.EndsWith(Path.DirectorySeparatorChar) ? fullFolder : fullFolder + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, Comparison);
    }


    // Absolute path with every link along the way replaced by what it points to
    public static string Canonicalize(string path) => Canonicalize(path, depth: 0);

    private static string Canonicalize(string path, int depth)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(full) ?? string.Empty;
        string current = root;

        string[] parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            current = Path.Combine(current, part);

            FileSystemInfo? target = ResolveLink(current);
            if (target != null)
            {
                current = depth >= MaxLinkDepth ? target.FullName : Canonicalize(target.FullName, depth + 1);
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    private static FileSystemInfo? ResolveLink(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.LinkTarget == null ? null : info.ResolveLinkTarget(returnFinalTarget: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not readable
            return null;
        }
    }
}

