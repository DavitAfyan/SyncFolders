# FolderSync

A small console program that keeps a **replica** folder identical to a **source** folder.
Synchronization is one-way (source -> replica) and runs periodically until you stop it.

Written in C# for .NET 10, no third-party libraries in the program itself.

## Usage

```
FolderSync --source <dir> --replica <dir> --interval <seconds> --log <file>
```

| Option       | Meaning                                                                         |
|--------------|---------------------------------------------------------------------------------|
| `--source`   | Folder to copy from. Must exist.                                                |
| `--replica`  | Folder that is made identical to the source. Created if it does not exist.      |
| `--interval` | Seconds between two synchronizations (whole number, 1 or more).                 |
| `--log`      | Log file. Everything that is logged is also printed to the console.             |

`--name value` and `--name=value` both work. `-h` / `--help` prints the usage text.

Run it from the repository root:

```
dotnet run --project src/FolderSync -- --source ./source --replica ./replica --interval 30 --log ./sync.log
```

Stop it with Ctrl+C (or SIGTERM). It finishes the file it is working on and exits.

> **Careful:** anything in the replica folder that is not in the source is **deleted**.
> That is what "identical copy" means, so don't point `--replica` at a folder with data you want to keep.

### Example log

```
2026-11-01 15:07:55 [INFO] Synchronizing '/data/source' to '/data/replica' every 30 s. Press Ctrl+C to stop.
2026-11-01 15:07:55 [INFO] Deleted file: stray.txt
2026-11-01 15:07:55 [INFO] Copied file: a.txt
2026-11-01 15:07:55 [INFO] Created folder: docs
2026-11-01 15:07:55 [INFO] Copied file: docs/b.txt
2026-11-01 15:07:55 [INFO] Synchronization finished: 1 folders created, 2 files copied, 0 files updated, 1 files deleted, 0 folders deleted, 0 errors
2026-11-01 15:08:25 [INFO] Updated file: a.txt
```

Every creation, copy, update and deletion is one line. Passes where nothing changed are not logged.

## How it works

Each pass walks the source tree and the replica tree together, one folder at a time:

1. Entries that exist only in the replica are deleted (a file where the source has a folder, or the other way round, counts as "different" and is replaced).
2. Folders missing from the replica are created, files missing from the replica are copied.
3. Files that exist on both sides are compared and copied again only if they differ.

To decide whether a file changed, the cheapest check that can answer wins:

- different size = changed;
- same size and same last-write time = unchanged (copies keep the source's timestamp, so this is the normal case for a file we already synced);
- same size, different timestamp = compare the **MD5** of both files. Same hash means the file was only touched, and the replica file's timestamp is aligned so it isn't hashed again.

The passes are scheduled with a delay *after* the previous pass finishes, so they never overlap even if a pass takes longer than the interval.

### Things that were decided on purpose

- **Safe copying.** A file is copied to a temporary file (`.foldersync-<guid>.tmp`) in the target folder and then moved into place, so the replica never contains a half-written file. A leftover temp file from a crash is removed by the next pass like any other stray file.
- **Errors don't stop the run.** If one file can't be read, written or deleted, that is logged as an error and the pass carries on with the rest. It is retried on the next pass.
- **A missing source never wipes the replica.** If the source folder can't be found (unplugged drive, for example) the pass fails with an error and the replica is not touched. The same goes for a source sub-folder that can't be listed: its counterpart in the replica is kept.
- **Symbolic links are skipped.** They are not followed and not copied (a warning is logged once per link). A link found in the replica is removed; only the link, never the folder it points to.
- **Dangerous setups are refused at startup:** replica equal to / inside the source, source inside the replica, and a log file located inside the replica (it would be deleted) or inside the source (it would be copied while being written).
- **Case.** Names are compared case-insensitively on Windows and macOS and case-sensitively elsewhere. If a file is renamed only by letter case, the replica gets the new spelling.
- What is guaranteed to be replicated is the folder structure, the file contents and the last-write times. Owners and other metadata are not touched on purpose.

## Tests

```
dotnet test
```

The tests (xUnit) cover argument parsing and validation, the logger, the synchronization itself (new / changed / unchanged / deleted files, nested and empty folders, file <-> folder swaps, symlinks, read-only files, unreadable files, a vanished source) and the periodic loop. Tests that need Unix file permissions or symlinks return early where the platform or user (Windows, root) can't provide them.

## Project layout

```
src/FolderSync/          the program
  Program.cs             entry point: arguments, log, Ctrl+C handling
  OptionsParser.cs       command line parsing and validation
  FolderSynchronizer.cs  one synchronization pass
  SyncLoop.cs            repeats the pass every N seconds
  FileAndConsoleLog.cs   logger (console + file)
tests/FolderSync.Tests/
```

