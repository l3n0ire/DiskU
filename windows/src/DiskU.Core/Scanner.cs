using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskU.Core;

/// <summary>
/// Walks a folder and builds the <see cref="FileNode"/> tree. Port of the Mac scanner with these
/// Windows rules:
/// <list type="bullet">
/// <item>Sizes are allocated bytes on disk (the directory entry's AllocationSize), like the Mac's
/// totalFileAllocatedSize. Files are never opened, so cloud placeholders are not hydrated.</item>
/// <item>Reparse points that are name surrogates (symlinks, junctions, volume mount points) become
/// zero-size leaves of kind Other and are never followed, so the scan never leaves the volume.
/// Other reparse points (OneDrive and other cloud files/folders, dedup) are scanned normally.</item>
/// <item>Hard links count once per scan: every further link to the same file ID is listed with size 0
/// (the Mac counts every link). Which link keeps the size is whichever is listed first.</item>
/// <item>Unreadable folders count as inaccessible and stay as empty folders. At a drive root,
/// "System Volume Information" is skipped silently when it can't be read (only SYSTEM can).</item>
/// </list>
/// </summary>
public static class Scanner
{
    /// <summary>Folders this shallow always fan their subfolders out to parallel tasks (as on the Mac).</summary>
    public const int ParallelDepth = 3;

    /// <summary>
    /// Scans <paramref name="path"/>. <paramref name="onProgress"/> is called from a background thread,
    /// never concurrently, about every 100 ms, once more with the final counts right before a successful
    /// return, and never after the returned task completes. Callers marshal to their UI thread.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The path doesn't exist or isn't a folder.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static Task<ScanResult> ScanAsync(string path, Action<ScanProgress>? onProgress = null, CancellationToken cancellationToken = default) =>
        ScanAsync(path, onProgress, ScanOptions.Default, cancellationToken);

    internal static async Task<ScanResult> ScanAsync(string path, Action<ScanProgress>? onProgress, ScanOptions options, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var walker = new Walker(NormalizeRoot(path), options, cancellationToken);

        using var tickerStop = new CancellationTokenSource();
        Task ticker = onProgress is null ? Task.CompletedTask : Task.Run(async () =>
        {
            while (true)
            {
                try { await Task.Delay(100, tickerStop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                onProgress(walker.Progress);
            }
        }, CancellationToken.None);

        FileNode root;
        try
        {
            root = await Task.Run(() => walker.ScanRootAsync(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Drain the ticker so no progress callback can land after the caller sees the outcome.
            tickerStop.Cancel();
            await ticker.ConfigureAwait(false);
        }
        onProgress?.Invoke(walker.Progress);
        return new ScanResult(root, walker.Inaccessible, clock.Elapsed);
    }

    /// <summary>Full path without a trailing separator (except drive roots). Throws if it isn't a folder.</summary>
    internal static string NormalizeRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (Path.GetPathRoot(full) != full) full = Path.TrimEndingDirectorySeparator(full);
        if (!Directory.Exists(full))
        {
            if (File.Exists(full)) throw new DirectoryNotFoundException(Strings.NotAFolder(full));
            throw new DirectoryNotFoundException(Strings.FolderNotFound(full));
        }
        return full;
    }

    private sealed class Walker(string rootPath, ScanOptions options, CancellationToken token)
    {
        private int _filesSeen;
        private long _bytesSeen;
        private string _currentPath = "";
        private int _inaccessible;
        private int _inFlight;
        private readonly bool _rootIsDrive = Path.GetPathRoot(rootPath) == rootPath;
        private readonly FileIdSet _seen = new();
        private ListFormat _format = ListFormat.Extd;

        public ScanProgress Progress => new(Volatile.Read(ref _filesSeen), Interlocked.Read(ref _bytesSeen), Volatile.Read(ref _currentPath));
        public int Inaccessible => Volatile.Read(ref _inaccessible);

        public async Task<FileNode> ScanRootAsync()
        {
            var root = new FileNode(rootPath, Kind.Folder);
            var (leaves, subdirectories) = Entries(rootPath, isRoot: true);
            root.Adopt(await ChildrenAsync(rootPath, leaves, subdirectories, depth: 0).ConfigureAwait(false));
            return root;
        }

        private async Task<FileNode> ScanAsync(string path, string name, int depth)
        {
            token.ThrowIfCancellationRequested();
            var (leaves, subdirectories) = Entries(path, isRoot: false);
            var node = FileNode.Named(name, Kind.Folder);
            node.Adopt(subdirectories.Count == 0 ? leaves : await ChildrenAsync(path, leaves, subdirectories, depth).ConfigureAwait(false));
            return node;
        }

        private FileNode ScanSync(string path, string name)
        {
            token.ThrowIfCancellationRequested();
            var (children, subdirectories) = Entries(path, isRoot: false);
            foreach (var dir in subdirectories) children.Add(ScanSync(Path.Join(path, dir), dir));
            var node = FileNode.Named(name, Kind.Folder);
            node.Adopt(children);
            return node;
        }

        /// <summary>
        /// Scans the subfolders of the folder at <paramref name="depth"/>. Like the Mac, folders shallower than
        /// <see cref="ParallelDepth"/> fan out to parallel tasks. Deeper folders also fan out while fewer than
        /// <see cref="ScanOptions.MaxInFlight"/> tasks run, so one huge deep subtree doesn't scan on a single thread.
        /// </summary>
        private async Task<List<FileNode>> ChildrenAsync(string path, List<FileNode> leaves, List<string> subdirectories, int depth)
        {
            var nodes = leaves;
            List<Task<FileNode>>? tasks = null;
            foreach (var dir in subdirectories)
            {
                string childPath = Path.Join(path, dir);
                bool parallel = depth < options.ParallelDepth
                    || (options.Adaptive && Volatile.Read(ref _inFlight) < options.MaxInFlight);
                if (parallel)
                {
                    Interlocked.Increment(ref _inFlight);
                    int childDepth = depth + 1;
                    (tasks ??= []).Add(Task.Run(async () =>
                    {
                        try { return await ScanAsync(childPath, dir, childDepth).ConfigureAwait(false); }
                        finally { Interlocked.Decrement(ref _inFlight); }
                    }, token));
                }
                else if (options.Adaptive)
                {
                    nodes.Add(await ScanAsync(childPath, dir, depth + 1).ConfigureAwait(false));
                }
                else
                {
                    nodes.Add(ScanSync(childPath, dir));
                }
            }
            if (tasks is not null) nodes.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
            return nodes;
        }

        [ThreadStatic] private static byte[]? t_buffer;


        /// <summary>
        /// Lists one folder: files and links become leaves; plain subfolders are returned by name for the
        /// caller to descend into.
        /// </summary>
        private unsafe (List<FileNode> Leaves, List<string> Subdirectories) Entries(string path, bool isRoot)
        {
            var leaves = new List<FileNode>();
            var subdirectories = new List<string>();
            using SafeFileHandle handle = Native.CreateFile(Native.Extended(path), Native.FILE_LIST_DIRECTORY,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                Interlocked.Increment(ref _inaccessible);
                return (leaves, subdirectories);
            }

            byte[] buffer = t_buffer ??= new byte[64 * 1024];
            // FileIdExtdDirectoryInfo has the reparse tag and 128-bit file ID and, unlike the "Both" class,
            // no 8.3 short names, which cost a lot on folders like WinSxS. Fall back for file systems without it.
            var format = _format;
            bool first = true;
            long bytes = 0;
            int files = 0;
            fixed (byte* p = buffer)
            {
                while (true)
                {
                    int infoClass = format switch
                    {
                        ListFormat.Extd => first ? Native.FileIdExtdDirectoryRestartInfo : Native.FileIdExtdDirectoryInfo,
                        ListFormat.Both => first ? Native.FileIdBothDirectoryRestartInfo : Native.FileIdBothDirectoryInfo,
                        _ => first ? Native.FileFullDirectoryRestartInfo : Native.FileFullDirectoryInfo,
                    };
                    if (!Native.GetFileInformationByHandleEx(handle, infoClass, p, (uint)buffer.Length))
                    {
                        int error = Marshal.GetLastPInvokeError();
                        if (first && format != ListFormat.Full && error is Native.ERROR_INVALID_PARAMETER or Native.ERROR_NOT_SUPPORTED or Native.ERROR_INVALID_LEVEL)
                        {
                            format++; // this file system lacks the class: try the next one, and start there next time
                            _format = format;
                            continue;
                        }
                        if (first && error is not (Native.ERROR_NO_MORE_FILES or Native.ERROR_FILE_NOT_FOUND))
                            Interlocked.Increment(ref _inaccessible);
                        break;
                    }
                    first = false;
                    int offset = 0;
                    while (true)
                    {
                        byte* entry = p + offset;
                        int next = *(int*)entry;
                        long allocation = *(long*)(entry + 48);
                        uint attributes = *(uint*)(entry + 56);
                        int nameBytes = *(int*)(entry + 60);
                        uint tag;
                        Int128 fileId;
                        char* namePtr;
                        switch (format)
                        {
                            case ListFormat.Extd: // FILE_ID_EXTD_DIR_INFO
                                tag = *(uint*)(entry + 68);
                                fileId = new Int128(*(ulong*)(entry + 80), *(ulong*)(entry + 72));
                                namePtr = (char*)(entry + 88);
                                break;
                            case ListFormat.Both: // FILE_ID_BOTH_DIR_INFO; EaSize holds the reparse tag (MS-FSCC 2.4)
                                tag = *(uint*)(entry + 64);
                                fileId = *(long*)(entry + 96);
                                namePtr = (char*)(entry + 104);
                                break;
                            default: // FILE_FULL_DIR_INFO
                                tag = *(uint*)(entry + 64);
                                fileId = 0;
                                namePtr = (char*)(entry + 68);
                                break;
                        }
                        var nameSpan = new ReadOnlySpan<char>(namePtr, nameBytes / 2);

                        if (!(nameSpan is "." or ".."))
                        {
                            string name = new(nameSpan);
                            bool isLink = (attributes & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0
                                && (tag & Native.REPARSE_TAG_NAME_SURROGATE_BIT) != 0;
                            if (isLink)
                            {
                                // Never followed. The link's own size, not added to bytesSeen (Mac quirk).
                                leaves.Add(FileNode.Named(name, Kind.Other, allocation, 1));
                                files++;
                            }
                            else if ((attributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0)
                            {
                                if (!(isRoot && _rootIsDrive && IsQuietlySkipped(path, name))) subdirectories.Add(name);
                            }
                            else
                            {
                                long size = fileId != Int128.Zero && !_seen.Add(fileId) ? 0 : allocation;
                                leaves.Add(FileNode.Named(name, Kinds.ForFile(name), size, 1));
                                bytes += size;
                                files++;
                            }
                        }
                        if (next == 0) break;
                        offset += next;
                    }
                }
            }
            Interlocked.Add(ref _filesSeen, files);
            Interlocked.Add(ref _bytesSeen, bytes);
            Volatile.Write(ref _currentPath, path);
            return (leaves, subdirectories);
        }

        /// <summary>
        /// "System Volume Information" at a drive root is readable only by SYSTEM. Counting it as inaccessible would show the banner on every drive scan, so it is left
        /// out when it can't be read and scanned normally when it can.
        /// </summary>
        private static bool IsQuietlySkipped(string drive, string name)
        {
            if (!name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) return false;
            using var probe = Native.CreateFile(Native.Extended(Path.Join(drive, name)), Native.FILE_LIST_DIRECTORY,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE | Native.FILE_SHARE_DELETE,
                IntPtr.Zero, Native.OPEN_EXISTING, Native.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            return probe.IsInvalid;
        }
    }

    /// <summary>A set of file IDs, striped so parallel tasks rarely contend.</summary>
    private sealed class FileIdSet
    {
        private const int Stripes = 64;
        private readonly HashSet<Int128>[] _sets = new HashSet<Int128>[Stripes];

        public FileIdSet()
        {
            for (int i = 0; i < Stripes; i++) _sets[i] = [];
        }

        public bool Add(Int128 id)
        {
            var set = _sets[(int)(((ulong)id ^ (ulong)(id >> 64)) * 0x9E3779B97F4A7C15UL >> 58)];
            lock (set) return set.Add(id);
        }
    }
}

/// <summary>Tuning knobs, internal for benchmarks.</summary>
internal sealed record ScanOptions(bool Adaptive, int MaxInFlight, int ParallelDepth = Scanner.ParallelDepth)
{
    public static ScanOptions Default { get; } = new(true, Environment.ProcessorCount * 2);
    public static ScanOptions MacLike { get; } = new(false, 0);
    public static ScanOptions Sequential { get; } = new(false, 0, 0);
}

/// <summary>Which directory-information class the scanner lists with, best first.</summary>
internal enum ListFormat { Extd, Both, Full }
