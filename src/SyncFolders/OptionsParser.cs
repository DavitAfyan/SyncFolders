using System.Globalization;

namespace FolderSync;

public static class OptionsParser
{
    public const string Usage = """
        Usage:
          FolderSync --source <dir> --replica <dir> --interval <seconds> --log <file>

        Options:
          --source    Folder to copy from. Must exist.
          --replica   Folder that is made identical to the source. Created if missing.
                      Anything in it that is not in the source gets deleted.
          --interval  Seconds to wait between two synchronizations (whole number, 1 or more).
          --log       Log file. Everything that is logged is also printed to the console.
          -h, --help  Show this text.

        Both "--name value" and "--name=value" are accepted.
        """;

    private static readonly string[] KnownOptions = { "source", "replica", "interval", "log" };

    public static SyncOptions Parse(string[] args)
    {
        var values = ReadNamedValues(args);

        var options = new SyncOptions(
            SourcePath: ToFullPath("source", Require(values, "source")),
            ReplicaPath: ToFullPath("replica", Require(values, "replica")),
            Interval: ParseInterval(Require(values, "interval")),
            LogPath: ToFullPath("log", Require(values, "log")));

        Validate(options);
        return options;
    }

    private static void Validate(SyncOptions options)
    {
        if (!Directory.Exists(options.SourcePath))
        {
            throw new UsageException($"Source folder '{options.SourcePath}' does not exist.");
        }

        // The replica is wiped to match the source, so these two must never overlap.
        if (PathUtil.IsSameOrInside(options.ReplicaPath, options.SourcePath))
        {
            throw new UsageException("Replica folder must not be the source folder or inside it.");
        }
        if (PathUtil.IsSameOrInside(options.SourcePath, options.ReplicaPath))
        {
            throw new UsageException("Source folder must not be inside the replica folder.");
        }

        if (Directory.Exists(options.LogPath))
        {
            throw new UsageException($"--log must be a file path, but '{options.LogPath}' is a folder.");
        }
        if (PathUtil.IsSameOrInside(options.LogPath, options.ReplicaPath))
        {
            throw new UsageException("Log file must not be inside the replica folder, it would be deleted by the synchronization.");
        }
        if (PathUtil.IsSameOrInside(options.LogPath, options.SourcePath))
        {
            throw new UsageException("Log file must not be inside the source folder, it would be copied while being written.");
        }
    }

    private static string ToFullPath(string option, string value)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new UsageException($"--{option} is not a valid path: {ex.Message}");
        }
    }

    private static Dictionary<string, string> ReadNamedValues(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new UsageException($"Unexpected argument '{arg}'.");
            }

            string name;
            string value;
            int equalsAt = arg.IndexOf('=');
            if (equalsAt > 0)
            {
                name = arg[2..equalsAt];
                value = arg[(equalsAt + 1)..];
            }
            else
            {
                name = arg[2..];
                if (i + 1 >= args.Length)
                {
                    throw new UsageException($"Option --{name} needs a value.");
                }
                value = args[++i];
            }

            if (!KnownOptions.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new UsageException($"Unknown option --{name}.");
            }
            if (!values.TryAdd(name, value))
            {
                throw new UsageException($"Option --{name} was given more than once.");
            }
        }

        return values;
    }

    private static string Require(Dictionary<string, string> values, string name)
    {
        if (!values.TryGetValue(name, out string? value))
        {
            throw new UsageException($"Missing required option --{name}.");
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException($"Option --{name} must not be empty.");
        }
        return value;
    }

    private static TimeSpan ParseInterval(string text)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds < 1)
        {
            throw new UsageException($"--interval must be a whole number of seconds, 1 or more (got '{text}').");
        }
        return TimeSpan.FromSeconds(seconds);
    }
}