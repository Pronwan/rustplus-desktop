using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;
using static RustPlusDesk.Views.MainWindow.ChatChannel;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MainChatComposerTests
{
    [TestMethod]
    public void Drafts_AreIndependentByLaneAndServer()
    {
        var state = new MainChatComposerState();
        var team = new MainChatComposerState.Destination(Team, "server-a");
        var clan = new MainChatComposerState.Destination(Clan, "server-a");
        var otherTeam = new MainChatComposerState.Destination(Team, "server-b");
        state.SetDraft(team, "team draft");
        state.SetDraft(clan, "clan draft");
        Assert.AreEqual("team draft", state.Draft(team));
        Assert.AreEqual("clan draft", state.Draft(clan));
        Assert.AreEqual("", state.Draft(otherTeam));
    }

    [TestMethod]
    public void SendCompletion_UpdatesOnlyItsDestination()
    {
        var state = new MainChatComposerState();
        var team = new MainChatComposerState.Destination(Team, "server");
        var global = new MainChatComposerState.Destination(Global, "account");
        state.SetDraft(team, "first");
        state.SetDraft(global, "second");
        Assert.IsTrue(state.TryBegin(team));
        Assert.IsFalse(state.TryBegin(team));
        Assert.IsTrue(state.CanCompose(global, restricted: false));
        state.Complete(team, "first", sent: false, error: "Not sent");
        Assert.AreEqual("first", state.Draft(team));
        Assert.AreEqual("Not sent", state.Error(team));
        Assert.AreEqual("second", state.Draft(global));
        Assert.IsNull(state.Error(global));
        Assert.IsTrue(state.TryBegin(team));
        state.Complete(team, "first", sent: true, error: null);
        Assert.AreEqual("", state.Draft(team));
        Assert.AreEqual("second", state.Draft(global));
    }

    [TestMethod]
    public void SendCompletion_DoesNotOverrideRestrictionsOrNewDraft()
    {
        var state = new MainChatComposerState();
        var global = new MainChatComposerState.Destination(Global, "account");
        state.SetDraft(global, "first");
        Assert.IsTrue(state.TryBegin(global));
        Assert.IsFalse(state.CanCompose(global, restricted: false));
        state.SetDraft(global, "replacement");
        state.Complete(global, "first", sent: true, error: null);
        Assert.IsFalse(state.CanCompose(global, restricted: true));
        Assert.AreEqual("replacement", state.Draft(global));
    }

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
