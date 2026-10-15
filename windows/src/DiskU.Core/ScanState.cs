namespace DiskU.Core;

/// <summary>Live counters of a running scan.</summary>
public readonly record struct ScanProgress(int FilesSeen = 0, long BytesSeen = 0, string CurrentPath = "");

/// <summary>Which case a <see cref="ScanState"/> is, for switching in XAML.</summary>
public enum ScanPhase { Idle, Scanning, Done, Failed }

/// <summary>
/// The single scan status (Mac: <c>enum ScanState</c>). Exactly one of the four cases; views switch on
/// <see cref="Phase"/> or pattern-match the nested records. Never add parallel booleans.
/// </summary>
public abstract record ScanState
{
    private ScanState() { }

    public abstract ScanPhase Phase { get; }

    public static ScanState Idle { get; } = new IdleState();

    public sealed record IdleState : ScanState
    {
        public override ScanPhase Phase => ScanPhase.Idle;
    }

    public sealed record Scanning(ScanProgress Progress) : ScanState
    {
        public override ScanPhase Phase => ScanPhase.Scanning;
    }

    public sealed record Done(FileNode Root, int Inaccessible) : ScanState
    {
        public override ScanPhase Phase => ScanPhase.Done;
    }

    public sealed record Failed(string Message) : ScanState
    {
        public override ScanPhase Phase => ScanPhase.Failed;
    }
}

/// <summary>What a finished scan returns.</summary>
public sealed record ScanResult(FileNode Root, int Inaccessible, TimeSpan Elapsed);
