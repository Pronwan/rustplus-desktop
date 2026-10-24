using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class SettingsSearchMatcherTests
{
    [TestMethod]
    public void MatchesEverySearchTermAcrossTitleAndKeywords()
    {
        Assert.IsTrue(SettingsSearchMatcher.Matches("gpu scale", "Map Performance", "GPU rendering scale cache"));
        Assert.IsFalse(SettingsSearchMatcher.Matches("gpu alerts", "Map Performance", "GPU rendering scale cache"));
    }
}
