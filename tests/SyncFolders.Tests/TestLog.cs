namespace FolderSync.Tests;

// Collects log messages in memory so tests can look at them
public sealed class TestLog : ILog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = new();

    // A copy of everything logged so far
    public List<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToList();
            }
        }
    }

    public void Info(string message) => Add("INFO " + message);

    public void Warn(string message) => Add("WARN " + message);

    public void Error(string message) => Add("ERROR " + message);

    private void Add(string line)
    {
        lock (_gate)
        {
            _lines.Add(line);
        }
    }
}