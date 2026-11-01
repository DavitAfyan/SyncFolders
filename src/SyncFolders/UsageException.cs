namespace FolderSync;

// Thrown when the command line is wrong
public sealed class UsageException : Exception
{
    public UsageException(string message) : base(message)
    {
    }
}