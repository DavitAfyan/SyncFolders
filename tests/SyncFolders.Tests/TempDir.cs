namespace FolderSync.Tests;

// A scratch folder that is deleted again when the test is done
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "foldersync-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    // Full path of something inside this folder
    public string At(string relativePath) =>
        System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    // Creates a sub folder and returns its full path 
    public string Folder(string relativePath)
    {
        string full = At(relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a test over a leftover temp folder
        }
    }
}