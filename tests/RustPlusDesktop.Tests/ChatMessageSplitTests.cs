using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

/// <summary>
/// Long command lists go out over several chat messages. Rust cuts a message at 128 characters,
/// and cutting it ourselves with "..." lost whatever came last - usually the Logic Engine rules.
/// </summary>
[TestClass]
public sealed class ChatMessageSplitTests
{
    [TestMethod]
    public void Short_lists_stay_one_message_with_the_header()
    {
        var messages = MainWindow.SplitChatMessages(new[] { "!pop", "!time" }, ", ", "COMMANDS: ");
        CollectionAssert.AreEqual(new[] { "COMMANDS: !pop, !time" }, messages);
    }

    [TestMethod]
    public void Long_lists_are_split_without_losing_an_item()
    {
        var items = Enumerable.Range(1, 12).Select(i => $"[Rule: Turret group {i}]: !turrets{i}").ToList();

        var messages = MainWindow.SplitChatMessages(items, " | ", "");

        Assert.IsTrue(messages.Count > 1);
        Assert.IsTrue(messages.All(m => m.Length <= 128), "every message fits Rust's limit");
        foreach (var item in items)
            Assert.IsTrue(messages.Any(m => m.Contains(item)), $"{item} is not lost");
    }

    [TestMethod]
    public void Nothing_to_say_is_no_message()
    {
        Assert.AreEqual(0, MainWindow.SplitChatMessages(new string[0], ", ", "COMMANDS: ").Count);
    }
}
