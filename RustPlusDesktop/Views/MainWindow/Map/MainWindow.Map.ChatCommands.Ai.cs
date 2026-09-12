using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.AiCompanion;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Views;

/// <summary>
/// The AI chat command: ask the companion from inside the game, and get the answer in chat.
///
/// It is the one command that costs money to answer, and the money is the owner's. That shapes
/// everything here: it is off for everyone but the owner until switched on, it is capped per
/// person per hour once it is, and it never spends anything before both of those have been
/// checked.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Rust truncates a long chat line, so the answer is asked for short and then cut anyway.
    /// Forty words is about two sentences, which is what fits and gets read.
    /// </summary>
    private const int ChatAiMaxWords = 40;

    /// <summary>Hard ceiling in characters, applied after the model has had its say.</summary>
    private const int ChatAiMaxChars = 300;

    /// <summary>When each author last asked, newest last, for the per-hour allowance.</summary>
    private readonly Dictionary<ulong, List<DateTime>> _chatAiAsks = new();

    /// <summary>
    /// Handles one <c>!ai</c>. Returns false when the message was not that command at all, so
    /// the caller can carry on matching.
    /// </summary>
    private async Task<bool> TryHandleAiCommand(
        ServerProfile profile, TeamChatMessage m, string command, string prefix,
        ChatChannel channel, Func<string, Task> reply)
    {
        var word = profile.CmdAi?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(word)) return false;

        // Unlike every other command this one takes an argument, so it matches on the first
        // word rather than on the whole line.
        if (command != word && !command.StartsWith(word + " ", StringComparison.Ordinal)) return false;

        // From the raw message, not the lower-cased copy the dispatcher matches on: names,
        // monuments and the question mark all matter to the model.
        var question = m.Text.Trim();
        question = question.Substring(Math.Min(question.Length, prefix.Length + word.Length)).Trim();

        if (question.Length == 0)
        {
            await reply(Helpers.Loc.Text("ChatAiUsage", "Ask a question after the command."));
            return true;
        }

        if (!await IsAiCommandAllowed(profile, m, channel, reply)) return true;

        var answer = await AiCompanionService.AskTextAsync(question, DockContextForChat(), ChatAiMaxWords);

        if (string.IsNullOrWhiteSpace(answer))
        {
            // Deliberately vague in chat. The reason is in the companion's history, where it
            // belongs — a rejected key or a spent quota is nobody else's business.
            await reply(Helpers.Loc.Text("ChatAiFailed", "The AI could not answer that."));
            return true;
        }

        RecordAiAsk(m.SteamId);
        await reply(Shorten(answer));
        return true;
    }

    /// <summary>
    /// Whether this author would be allowed to use it, for the command list.
    ///
    /// The allowance is deliberately not checked here: someone who has used their five for the
    /// hour still has the command, and dropping it from the list would read as it having been
    /// taken away.
    /// </summary>
    private bool IsAiCommandListable(ServerProfile profile, TeamChatMessage m, ChatChannel channel)
    {
        if (!AiCompanionStore.HasKey || !SupabaseAuthManager.IsPremium) return false;
        if (m.SteamId == _mySteamId) return true;

        return channel == ChatChannel.Clan ? profile.ClanCommandsAllowAi : profile.ChatAiAllowTeammates;
    }

    /// <summary>Everything that has to be true before a question is paid for.</summary>
    private async Task<bool> IsAiCommandAllowed(
        ServerProfile profile, TeamChatMessage m, ChatChannel channel, Func<string, Task> reply)
    {
        if (!AiCompanionStore.HasKey)
        {
            await reply(Helpers.Loc.Text("ChatAiNoKey", "No AI is set up on this client."));
            return false;
        }

        if (!SupabaseAuthManager.IsPremium)
        {
            await reply(Helpers.Loc.Text("ChatAiSupporter", "Asking the AI from chat is a supporter feature."));
            return false;
        }

        // The owner is never gated and never counted. It is their key, their client and their
        // bill; the settings below exist to control everybody else.
        if (m.SteamId == _mySteamId) return true;

        bool allowed = channel == ChatChannel.Clan
            ? profile.ClanCommandsAllowAi
            : profile.ChatAiAllowTeammates;

        if (!allowed)
        {
            AppendLog($"[ChatCommand] AI question from {m.Author} ignored: not allowed for {channel}.");
            return false;   // silently: an answer here would only advertise a switch they cannot flip
        }

        if (!HasAiAllowanceLeft(profile, m.SteamId))
        {
            await reply(string.Format(
                Helpers.Loc.Text("ChatAiRateLimited", "{0} has used their AI questions for this hour."),
                m.Author));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether this author has an ask left in the current hour.
    ///
    /// A rolling hour rather than a clock hour, so the allowance cannot be doubled by asking
    /// twice either side of the top of the hour.
    /// </summary>
    private bool HasAiAllowanceLeft(ServerProfile profile, ulong steamId)
    {
        int limit = profile.ChatAiPerHour;
        if (limit <= 0) return true;

        if (!_chatAiAsks.TryGetValue(steamId, out var asks)) return true;

        var cutoff = DateTime.UtcNow.AddHours(-1);
        asks.RemoveAll(at => at < cutoff);

        return asks.Count < limit;
    }

    private void RecordAiAsk(ulong steamId)
    {
        if (steamId == _mySteamId) return;

        if (!_chatAiAsks.TryGetValue(steamId, out var asks))
            _chatAiAsks[steamId] = asks = new List<DateTime>();

        asks.Add(DateTime.UtcNow);
    }

    /// <summary>
    /// What the app knows, for a question asked from chat.
    ///
    /// Less than the dock sends: no screenshot and no recording, so the clock and the population
    /// are the whole of it.
    /// </summary>
    private string? DockContextForChat()
    {
        try
        {
            var (time, isDay, _) = DockServerTime;
            var (current, max, _) = DockPopulation;

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(time)) parts.Add($"in-game time {time} ({(isDay ? "day" : "night")})");
            if (max > 0) parts.Add($"{current} of {max} players on the server");

            return parts.Count == 0 ? null : string.Join(", ", parts) + ".";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Cuts the answer to something the game will actually show.
    ///
    /// At a sentence end where there is one, because a reply that stops mid-clause reads as a
    /// broken app rather than a long answer.
    /// </summary>
    private static string Shorten(string answer)
    {
        answer = answer.Replace("\r", " ").Replace("\n", " ").Trim();
        while (answer.Contains("  ")) answer = answer.Replace("  ", " ");

        if (answer.Length <= ChatAiMaxChars) return answer;

        var cut = answer.Substring(0, ChatAiMaxChars);
        int stop = cut.LastIndexOfAny(new[] { '.', '!', '?' });

        return stop > ChatAiMaxChars / 2
            ? cut.Substring(0, stop + 1)
            : cut.TrimEnd() + "…";
    }
}
