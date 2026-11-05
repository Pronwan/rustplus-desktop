using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ArchipelRelayTests
{
    [DataTestMethod]
    [DataRow("bases?limit=24&offset=0&public_only=true&sort=likes")]
    [DataRow("bases?limit=24&tags=Small+group+%284-6%29&name=asdasd")]
    [DataRow("bases/featured")]
    [DataRow("trending/week")]
    [DataRow("preview/data/abcd-1234")]
    [DataRow("bases/0123456789abcdef01234567/stats")]
    public void PublicReadsPreservePathAndEncodedFilters(string path)
    {
        Assert.IsTrue(MainWindow.TryGetArchipelRelayPath(new Uri("https://rustplus-api.invalid/archipel/" + path), "GET", out var actual));
        Assert.AreEqual(path, actual);
        Assert.IsTrue(MainWindow.TryGetArchipelRelayPath(new Uri("https://rustplus3d.local/archipel/" + path), "GET", out actual));
        Assert.AreEqual(path, actual, "Legacy map requests retain the same read-only policy.");
    }

    [DataTestMethod]
    [DataRow("https://rustplus-api.invalid/archipel/bases", "POST")]
    [DataRow("http://rustplus-api.invalid/archipel/bases", "GET")]
    [DataRow("https://rustplus-api.invalid:444/archipel/bases", "GET")]
    [DataRow("https://evil.example/archipel/bases", "GET")]
    [DataRow("https://rustplus-api.invalid.evil.example/archipel/bases", "GET")]
    [DataRow("https://user@rustplus-api.invalid/archipel/bases", "GET")]
    [DataRow("https://rustplus-api.invalid/archipel/admin", "GET")]
    [DataRow("https://rustplus-api.invalid/archipel/bases/upload", "GET")]
    [DataRow("https://rustplus-api.invalid/archipel/preview/data/a%2Fb", "GET")]
    [DataRow("https://rustplus-api.invalid/archipel/../secrets", "GET")]
    [DataRow("https://rustplus-api.invalid/index.html", "GET")]
    public void UnapprovedRequestsCannotReachUpstream(string url, string method)
    {
        Assert.IsFalse(MainWindow.TryGetArchipelRelayPath(new Uri(url), method, out var path));
        Assert.AreEqual("", path);
    }
}
