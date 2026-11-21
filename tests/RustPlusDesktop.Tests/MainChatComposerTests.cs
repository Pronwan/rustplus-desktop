using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MainChatComposerTests
{
    [TestMethod]
    public void ClosingDrawer_IsWiredToClearSearch()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var code = File.ReadAllText(Path.Combine(root, "src/RustPlusDesktop/Views/MainWindow/Map/MainWindow.Map.Chat.cs"));
        var start = code.IndexOf("private void CloseChatOverlay()", StringComparison.Ordinal);
        var end = code.IndexOf("private void BtnCloseChatOverlay_Click", start, StringComparison.Ordinal);
        StringAssert.Contains(code[start..end], "CloseChatSearch();");
    }
}
