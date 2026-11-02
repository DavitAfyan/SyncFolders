using System.Text;

namespace FolderSync;

// Writes every message to the console and appends it to a log file
public sealed class FileAndConsoleLog : ILog, IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _file;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    // track if writing to file failed so we don't spam the console with errors 
    private bool _fileFailed;

    public FileAndConsoleLog(string logPath, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        // making sure the directory exists before opening stream
        string? directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _file = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _stdout = stdout ?? Console.Out;
        _stderr = stderr ?? Console.Error;
    }

    public void Info(string message)
    {
        Write("INFO", message, _stdout);
    }
    public void Warn(string message)
    {
        Write("WARN", message, _stderr);
    }
    public void Error(string message)
    {
        Write("ERROR", message, _stderr);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                _file.Dispose();
            }
            catch (IOException)
            {
                // Flushing can fail if the disk went away
            }
        }
    }


    // Core write method
    private void Write(string level, string message, TextWriter console)
    {
        // format: date time [LEVEL] message
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        lock (_gate)
        {
            try
            {
                console.WriteLine(line);
            }
            catch (IOException)
            {
                // Closed stdout or a broken pipe. The file log is still worth keeping.
            }


            try
            {
                _file.WriteLine(line);
                _fileFailed = false;
            }
            catch (IOException ex)
            {
                // Losing the log file shouldn't stop the synchronization
                // Notify the user and continue
                if (!_fileFailed)
                {
                    _stderr.WriteLine($"Could not write to the log file: {ex.Message}");
                    _fileFailed = true;
                }
            }
        }
    }
}