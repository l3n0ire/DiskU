using System.Net;
using System.Text.Json;
using DiskU.Core;

namespace DiskU.Core.Tests;

[TestClass]
public sealed class UpdateTests
{
    private static AppVersion V(string s) => AppVersion.Parse(s) ?? throw new AssertFailedException($"{s} didn't parse");

    [TestMethod]
    public void VersionsCompareNumberByNumber()
    {
        Assert.IsTrue(V("v1.0.10") > V("1.0.9"));
        Assert.IsTrue(V("v1.1") > V("v1.0.3"));
        Assert.IsTrue(V("1.0") == V("v1.0.0"));
        Assert.AreEqual("2.0.0", V("v2.0.0").ToString());
        Assert.IsNull(AppVersion.Parse("v1.0-beta"));
        Assert.IsNull(AppVersion.Parse("latest"));
    }

    [TestMethod]
    public void ReleaseDecodesFromGitHubResponse()
    {
        var release = Release.Parse("""{"tag_name": "v1.0.4", "html_url": "https://github.com/l3n0ire/DiskU/releases/tag/v1.0.4", "draft": false}""");
        Assert.AreEqual("1.0.4", release.Version.ToString());
        Assert.AreEqual("https://github.com/l3n0ire/DiskU/releases/tag/v1.0.4", release.Url.AbsoluteUri);
        Assert.ThrowsExactly<JsonException>(() => Release.Parse("""{"tag_name": "nightly", "html_url": "https://x"}"""));
    }

    // ---- Windows additions ----

    [TestMethod]
    public void VersionParsingQuirksMatchSwift()
    {
        Assert.IsTrue(V("1..0") == V("1.0"));          // empty pieces dropped, like Swift split
        Assert.IsTrue(V("+1.0") == V("1"));            // Swift Int() accepts a sign
        Assert.IsNull(AppVersion.Parse("v"));
        Assert.IsNull(AppVersion.Parse(""));
        Assert.IsNull(AppVersion.Parse("vv1.0"));      // only one leading v is stripped
        Assert.IsTrue(V("1.0.0") <= V("1.0"));
        Assert.AreEqual(V("1.0").GetHashCode(), V("1.0.0").GetHashCode());
        Assert.IsTrue(V("1.0.4") < V("1.0.5"));
        Assert.IsFalse(V("1.0.4") < V("1.0.4"));
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private const string Latest = """{"tag_name": "v1.0.5", "html_url": "https://github.com/l3n0ire/DiskU/releases/tag/v1.0.5"}""";

    [TestMethod]
    public async Task CheckerOffersANewerReleaseWithUserAgent()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Latest);
        var checker = new UpdateChecker(handler);
        var changed = new List<string?>();
        checker.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await checker.CheckAsync(V("1.0.4"));

        Assert.AreEqual("1.0.5", checker.Available?.Version.ToString());
        Assert.IsTrue(checker.ShowBanner);
        Assert.AreEqual("DiskU 1.0.5 is available.", Strings.UpdateAvailable(checker.Available!.Version));
        Assert.AreEqual(UpdateChecker.LatestReleaseUrl, handler.Request!.RequestUri);
        Assert.AreEqual("DiskU/1.0.4", handler.Request.Headers.UserAgent.ToString());
        Assert.AreEqual("application/vnd.github+json", handler.Request.Headers.Accept.ToString());
        CollectionAssert.Contains(changed, nameof(UpdateChecker.Available));

        checker.Dismissed = true;
        Assert.IsFalse(checker.ShowBanner);
    }

    [TestMethod]
    public async Task CheckerStaysSilentWhenCurrentOrFailing()
    {
        var same = new UpdateChecker(new FakeHandler(HttpStatusCode.OK, Latest));
        await same.CheckAsync(V("1.0.5"));
        Assert.IsNull(same.Available);

        var limited = new UpdateChecker(new FakeHandler(HttpStatusCode.Forbidden, Latest));
        await limited.CheckAsync(V("1.0.0"));
        Assert.IsNull(limited.Available);

        var garbage = new UpdateChecker(new FakeHandler(HttpStatusCode.OK, "<html>"));
        await garbage.CheckAsync(V("1.0.0"));
        Assert.IsNull(garbage.Available);

        var nightly = new UpdateChecker(new FakeHandler(HttpStatusCode.OK, """{"tag_name": "nightly", "html_url": "https://x"}"""));
        await nightly.CheckAsync(V("1.0.0"));
        Assert.IsNull(nightly.Available);

        var unversioned = new FakeHandler(HttpStatusCode.OK, Latest);
        await new UpdateChecker(unversioned).CheckAsync((AppVersion?)null);
        Assert.IsNull(unversioned.Request, "no request without a running version");
    }
}
