namespace FolderSync;

internal static class PathUtil
{
    // Linux is not case sencitive
    private static readonly bool CaseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static StringComparison Comparison =>
        CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer NameComparer =>
        CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // True if path is the folder itself or lies somewhere below it
    public static bool IsSameOrInside(string path, string folder)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string fullFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        if (fullPath.Equals(fullFolder, Comparison))
        {
            return true;
        }

        // Compare with a trailing separator
        string prefix = fullFolder.EndsWith(Path.DirectorySeparatorChar) ? fullFolder : fullFolder + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, Comparison);
    }
}