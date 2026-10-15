using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DiskU.Core;

/// <summary>
/// A release version such as <c>v1.2.3</c>, compared number by number so 1.0.10 is newer than 1.0.9,
/// and 1.0 equals 1.0.0. Named AppVersion to avoid System.Version.
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    public IReadOnlyList<int> Parts { get; }

    private AppVersion(int[] parts) => Parts = parts;

    /// <summary>
    /// Strips one leading "v", splits on "." dropping empty pieces (so "1..0" is 1.0), and requires every
    /// piece to be an integer (a leading sign is allowed, as Swift's Int() does). Null otherwise.
    /// </summary>
    public static AppVersion? Parse(string? text)
    {
        if (text is null) return null;
        string trimmed = text.StartsWith('v') ? text[1..] : text;
        var pieces = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 0) return null;
        var parts = new int[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            if (!int.TryParse(pieces[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parts[i])) return null;
        }
        return new AppVersion(parts);
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        int count = Math.Max(Parts.Count, other.Parts.Count);
        for (int i = 0; i < count; i++)
        {
            int x = i < Parts.Count ? Parts[i] : 0;
            int y = i < other.Parts.Count ? other.Parts[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is AppVersion v && Equals(v);

    public override int GetHashCode()
    {
        // Equal versions differ only by trailing zeros, so hash without them.
        int last = Parts.Count - 1;
        while (last >= 0 && Parts[last] == 0) last--;
        var hash = new HashCode();
        for (int i = 0; i <= last; i++) hash.Add(Parts[i]);
        return hash.ToHashCode();
    }

    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;
    public static bool operator ==(AppVersion? a, AppVersion? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(AppVersion? a, AppVersion? b) => !(a == b);

    /// <summary>Pieces joined by dots, without the "v": "v2.0.0" → "2.0.0".</summary>
    public override string ToString() => string.Join('.', Parts);
}

/// <summary>A GitHub release: its version (from tag_name) and page (html_url).</summary>
public sealed record Release(AppVersion Version, Uri Url)
{
    /// <summary>Decodes a GitHub "latest release" response. Throws <see cref="JsonException"/> when it isn't one.</summary>
    public static Release Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
            || !root.TryGetProperty("html_url", out var html) || html.ValueKind != JsonValueKind.String)
            throw new JsonException("missing tag_name or html_url");
        var version = AppVersion.Parse(tag.GetString()) ?? throw new JsonException($"not a version: {tag.GetString()}");
        if (!Uri.TryCreate(html.GetString(), UriKind.Absolute, out var url)) throw new JsonException("html_url is not a URL");
        return new Release(version, url);
    }
}

/// <summary>
/// Looks up the latest GitHub release once and remembers it if it is newer than the running app.
/// Silent on any failure. Call <see cref="CheckAsync"/> from the UI thread; it resumes there.
/// </summary>
public sealed class UpdateChecker : INotifyPropertyChanged
{
    public static readonly Uri LatestReleaseUrl = new("https://api.github.com/repos/l3n0ire/DiskU/releases/latest");
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpMessageHandler? _handler;
    private Release? _available;
    private bool _dismissed;

    /// <param name="handler">Injected for tests; null uses a default handler.</param>
    public UpdateChecker(HttpMessageHandler? handler = null) => _handler = handler;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>A newer release, or null.</summary>
    public Release? Available
    {
        get => _available;
        private set { _available = value; OnChanged(); OnChanged(nameof(ShowBanner)); }
    }

    /// <summary>In memory only; the banner comes back next launch.</summary>
    public bool Dismissed
    {
        get => _dismissed;
        set { _dismissed = value; OnChanged(); OnChanged(nameof(ShowBanner)); }
    }

    public bool ShowBanner => Available is not null && !Dismissed;

    /// <summary>The running app's version from the entry assembly's informational version, e.g. 1.0.4.</summary>
    public static AppVersion? CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly();
            string? text = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (text is not null)
            {
                int plus = text.IndexOf('+'); // strip "+commit" source-link metadata
                if (plus >= 0) text = text[..plus];
            }
            return AppVersion.Parse(text ?? assembly?.GetName().Version?.ToString(3));
        }
    }

    /// <summary>The User-Agent GitHub requires, e.g. "DiskU/1.0.4".</summary>
    public static string UserAgent(AppVersion? current) => $"DiskU/{current?.ToString() ?? "0"}";

    public Task CheckAsync(CancellationToken cancellationToken = default) => CheckAsync(CurrentVersion, cancellationToken);

    public async Task CheckAsync(AppVersion? current, CancellationToken cancellationToken = default)
    {
        if (current is null) return;
        Release? latest;
        try
        {
            using var client = _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
            client.Timeout = Timeout;
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd(UserAgent(current));
            using var response = await client.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode != 200) return;
            latest = Release.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or IOException)
        {
            return; // offline, rate limited, or not a release: never bother the user
        }
        Available = current < latest.Version ? latest : null;
    }

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
