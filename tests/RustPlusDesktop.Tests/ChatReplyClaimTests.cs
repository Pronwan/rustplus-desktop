using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

/// <summary>
/// The app claims a chat command before answering it, so it and the cloud never both answer.
///
/// The claim must fail open: whenever the platform cannot settle it, the app answers as it
/// always did. A claim that silenced the desktop on its own would trade a duplicate reply
/// for no reply at all.
/// </summary>
[TestClass]
public class ChatReplyClaimTests
{
    [TestMethod]
    public async Task Claim_AnswersWhenTheMessageCarriesNoServerTime()
    {
        Assert.IsTrue(await CloudSessionsApi.ClaimChatReplyAsync("1.2.3.4:28082", 76561190000000001, "!pop", 0));
    }

    [TestMethod]
    public async Task Claim_AnswersWithoutAServerOrSender()
    {
        Assert.IsTrue(await CloudSessionsApi.ClaimChatReplyAsync(null, 76561190000000001, "!pop", 1_700_000_000));
        Assert.IsTrue(await CloudSessionsApi.ClaimChatReplyAsync("1.2.3.4:28082", 0, "!pop", 1_700_000_000));
    }

    [TestMethod]
    public async Task Claim_AnswersWhenNotSignedIn()
    {
        // No cloud session in the test host: nothing to ask, so the app answers.
        Assert.IsTrue(await CloudSessionsApi.ClaimChatReplyAsync("1.2.3.4:28082", 76561190000000001, "!pop", 1_700_000_000));
    }

    [TestMethod]
    public void TeamChatMessage_KeepsTheServerTimeApartFromTheDisplayTimestamp()
    {
        var withTime = new TeamChatMessage(System.DateTime.UtcNow, "Ana", 1, "!pop", ServerTime: 1_700_000_000);
        var without = new TeamChatMessage(System.DateTime.UtcNow, "Ana", 1, "!pop");

        Assert.AreEqual(1_700_000_000L, withTime.ServerTime);
        Assert.IsNull(without.ServerTime);
    }
}
