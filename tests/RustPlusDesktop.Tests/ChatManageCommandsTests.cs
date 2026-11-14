using System.Collections.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

/// <summary>
/// !mute, !unmute and !device as the app answers them: the same rules the cloud applies.
/// </summary>
[TestClass]
public class ChatManageCommandsTests
{
    [DataTestMethod]
    [DataRow("15", 900)]
    [DataRow("15m", 900)]
    [DataRow("30s", 30)]
    [DataRow("2h", 7200)]
    [DataRow("1d", 86400)]
    [DataRow("90 min", 5400)]
    public void Reads_mute_durations(string text, int seconds)
    {
        Assert.IsTrue(ChatManageCommands.TryParseDuration(text, out var duration));
        Assert.AreEqual(TimeSpan.FromSeconds(seconds), duration);
    }

    [TestMethod]
    public void A_bare_mute_is_open_ended_and_nonsense_is_refused_and_long_mutes_are_capped()
    {
        Assert.IsTrue(ChatManageCommands.TryParseDuration("", out var none));
        Assert.IsNull(none);

        Assert.IsFalse(ChatManageCommands.TryParseDuration("soon", out _));
        Assert.IsFalse(ChatManageCommands.TryParseDuration("0", out _));
        Assert.IsFalse(ChatManageCommands.TryParseDuration("-5m", out _));

        Assert.IsTrue(ChatManageCommands.TryParseDuration("30d", out var capped));
        Assert.AreEqual(ChatManageCommands.MaxMute, capped);
    }

    [TestMethod]
    public void A_timed_mute_runs_out_by_its_timestamp()
    {
        var now = new DateTime(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.IsTrue(ChatManageCommands.IsMuteActive(true, null, now));
        Assert.IsTrue(ChatManageCommands.IsMuteActive(true, now.AddMinutes(1), now));
        Assert.IsFalse(ChatManageCommands.IsMuteActive(true, now.AddSeconds(-1), now));
        Assert.IsFalse(ChatManageCommands.IsMuteActive(false, null, now));
        Assert.AreEqual("15m", ChatManageCommands.Describe(TimeSpan.FromMinutes(15)));
        Assert.AreEqual("1d 3h", ChatManageCommands.Describe(TimeSpan.FromHours(27)));
    }

    private static ServerProfile Profile()
    {
        var profile = new ServerProfile
        {
            Devices = new ObservableCollection<SmartDevice>
            {
                new() { EntityId = 1, Name = "Lights", Kind = "SmartSwitch" },
                new() { EntityId = 5, Name = "Raid Alarm", Kind = "SmartAlarm" },
                new() { EntityId = 2, Name = "Gate", Alias = "Front Gate", Kind = "SmartSwitch" },
            },
        };
        profile.SwitchCommandMappings.Clear();
        profile.SwitchCommandMappings.Add(new ChatCommandMapping { EntityId = 1, Command = "lights" });
        return profile;
    }

    [TestMethod]
    public void Devices_are_found_by_list_number_command_word_name_or_id()
    {
        var profile = Profile();

        Assert.AreEqual(2u, ChatManageCommands.FindDevice(profile, "#2")!.EntityId, "#2 is the second switch");
        Assert.AreEqual(1u, ChatManageCommands.FindDevice(profile, "lights")!.EntityId);
        Assert.AreEqual(2u, ChatManageCommands.FindDevice(profile, "frontgate")!.EntityId);
        Assert.AreEqual(5u, ChatManageCommands.FindDevice(profile, "5")!.EntityId);
        Assert.IsNull(ChatManageCommands.FindDevice(profile, "#9"));
    }

    [TestMethod]
    public void Command_words_are_validated()
    {
        var profile = Profile();

        Assert.IsNull(ChatManageCommands.CommandWordProblem(profile, 2, "gate"));
        StringAssert.Contains(ChatManageCommands.CommandWordProblem(profile, 2, "mute"), "already a command");
        StringAssert.Contains(ChatManageCommands.CommandWordProblem(profile, 2, "time"), "already a command");
        StringAssert.Contains(ChatManageCommands.CommandWordProblem(profile, 2, "lights"), "another device");
        Assert.IsNull(ChatManageCommands.CommandWordProblem(profile, 1, "lights"), "a device may keep its own word");
        StringAssert.Contains(ChatManageCommands.CommandWordProblem(profile, 2, "9lives"), "must not start with a digit");
        StringAssert.Contains(ChatManageCommands.CommandWordProblem(profile, 2, new string('x', 33)), "1 to 32");
    }

    [TestMethod]
    public void Device_commands_parse_and_bind()
    {
        var parsed = ChatManageCommands.ParseDevice("#2 name Front Gate Two");
        Assert.IsNotNull(parsed);
        Assert.AreEqual("#2", parsed.Value.Ref);
        Assert.IsTrue(parsed.Value.IsName);
        Assert.AreEqual("Front Gate Two", parsed.Value.Value);
        Assert.IsNull(ChatManageCommands.ParseDevice("gate"));

        Assert.IsNull(ChatManageCommands.CleanName(new string('x', 41)));
        Assert.AreEqual("Gate", ChatManageCommands.CleanName(" Gate\u0007 "));

        var profile = Profile();
        var gate = ChatManageCommands.FindDevice(profile, "#2")!;
        ChatManageCommands.BindCommand(profile, gate, "door");
        ChatManageCommands.BindCommand(profile, ChatManageCommands.FindDevice(profile, "lights")!, "lamp");

        Assert.AreEqual("door", profile.SwitchCommandMappings.Single(m => m.EntityId == 2).Command);
        Assert.AreEqual("lamp", profile.SwitchCommandMappings.Single(m => m.EntityId == 1).Command);
    }
}
