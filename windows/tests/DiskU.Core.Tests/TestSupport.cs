using System.Diagnostics;
using DiskU.Core;

namespace DiskU.Core.Tests;

/// <summary>Temp-folder fixtures. Tests never touch real user files.</summary>
internal static class Fixture
{
    public const int Block = 4096;

    /// <summary>A fresh, empty folder under %TEMP%.</summary>
    public static string TempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Port of the Mac makeFixture(). Allocated sizes are exact because NTFS allocates 4 KiB clusters and
    /// every file is a whole number of blocks (all large enough not to live inside the MFT record).
    /// Adaptations: Tool.app is a plain folder (Windows has no bundles); photos/deep/loop is a junction to
    /// the root (the Mac uses a symlink to ../..); movie-alias.mov is a file symlink, or a junction when
    /// symlinks need privileges this account lacks. Both are reparse points, which the scanner never follows.
    /// </summary>
    public static string Make()
    {
        string root = TempDir("DiskUTests");
        Write(root, "movie.mov", 5);
        Write(root, @"photos\a.png", 2);
        Write(root, @"photos\b.jpg", 1);
        Write(root, @"photos\deep\notes.txt", 1);
        Write(root, @"Tool.app\Contents\MacOS\Tool", 3);
        Write(root, @"Tool.app\Contents\Info.plist", 1);
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        Junction(Path.Combine(root, @"photos\deep\loop"), root);
        FileSymlinkOrJunction(Path.Combine(root, "movie-alias.mov"), Path.Combine(root, "movie.mov"), root);
        return root;
    }

    public static void Write(string root, string relative, int blocks)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[blocks * Block];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(unchecked((uint)i * 2654435761u) >> 7);
        File.WriteAllBytes(path, bytes);
    }

    public static void Junction(string link, string target) => Cmd($"mklink /J \"{link}\" \"{target}\"");

    public static void HardLink(string link, string target) => Cmd($"mklink /H \"{link}\" \"{target}\"");

    private static void FileSymlinkOrJunction(string link, string fileTarget, string folderTarget)
    {
        try
        {
            File.CreateSymbolicLink(link, fileTarget);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Junction(link, folderTarget); // no Developer Mode: still a reparse point named like the Mac's alias
        }
    }

    private static void Cmd(string command)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(30_000)) throw new TimeoutException(command);
        if (process.ExitCode != 0) throw new IOException($"{command} failed: {error}");
    }

    /// <summary>Deletes a fixture. Directory.Delete removes junctions and symlinks without following them.</summary>
    public static void Remove(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static string[] Names(this FileNode node) => node.Children.Select(c => c.Name).ToArray();

    public static FileNode Child(this FileNode node, string name) =>
        node.Children.FirstOrDefault(c => c.Name == name) ?? throw new AssertFailedException($"{node.Name} has no child {name}");
}

/// <summary>
/// Stands in for the UI thread: DiskStore posts here and the test runs the queue, like the Mac tests'
/// main actor. Keeps state changes on the test thread.
/// </summary>
internal sealed class ManualDispatcher
{
    private readonly Queue<Action> _queue = new();

    public void Post(Action action)
    {
        lock (_queue) _queue.Enqueue(action);
    }

    public void RunPending()
    {
        while (true)
        {
            Action? next;
            lock (_queue) if (!_queue.TryDequeue(out next)) return;
            next();
        }
    }

    /// <summary>Runs posted work until <paramref name="done"/> holds or the deadline passes. Every poll has a deadline.</summary>
    public async Task<bool> RunUntilAsync(Func<bool> done, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            RunPending();
            if (done()) return true;
            await Task.Delay(10);
        }
        RunPending();
        return done();
    }
}
