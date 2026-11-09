using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

/// <summary>
/// What the cloud is told about a server's rules: the worker reads exactly this shape, and it
/// has no groups and no paired positions of its own, so the app resolves both on the way out.
/// </summary>
[TestClass]
public sealed class CloudAutomationSyncTests
{
    private static JsonElement Payload(ServerProfile profile)
        => JsonDocument.Parse(JsonSerializer.Serialize(CloudAutomationSync.BuildPayload(profile, "1.2.3.4-28082"))).RootElement;

    private static SmartDevice Switch(uint id) => new() { EntityId = id, Kind = "SmartSwitch" };

    [TestMethod]
    public void A_toggle_aimed_at_a_group_is_sent_as_the_switches_in_it()
    {
        var group = new SmartDevice
        {
            IsGroup = true,
            Alias = "Turrets",
            Children = new ObservableCollection<SmartDevice>
            {
                Switch(11),
                new() { EntityId = 12, Kind = "SmartAlarm" },
                new() { IsGroup = true, Alias = "Inner", Children = new ObservableCollection<SmartDevice> { Switch(13) } },
            },
        };
        var profile = new ServerProfile { Devices = new ObservableCollection<SmartDevice> { group, Switch(20) } };
        profile.LogicRules = new List<LogicRule>
        {
            new()
            {
                Id = "r1", IsEnabled = true, TriggerType = "ChatCommand", TriggerCommand = "lockdown",
                Steps = new ObservableCollection<LogicStep>
                {
                    new() { StepType = "Toggle", TargetGroupName = "Turrets", ToggleState = true },
                    new() { StepType = "Toggle", TargetEntityId = 20, ToggleState = null },
                    new() { StepType = "Wait", WaitSeconds = 5 },
                },
            },
        };

        var steps = Payload(profile).GetProperty("logic").GetProperty("rules")[0].GetProperty("steps");

        CollectionAssert.AreEqual(new uint[] { 11, 13 },
            steps[0].GetProperty("target_entity_ids").EnumerateArray().Select(e => e.GetUInt32()).ToArray());
        Assert.AreEqual(true, steps[0].GetProperty("toggle_state").GetBoolean());
        CollectionAssert.AreEqual(new uint[] { 20 },
            steps[1].GetProperty("target_entity_ids").EnumerateArray().Select(e => e.GetUInt32()).ToArray());
        // null means "invert", and has to arrive as null rather than as false.
        Assert.AreEqual(JsonValueKind.Null, steps[1].GetProperty("toggle_state").ValueKind);
        Assert.AreEqual(0, steps[2].GetProperty("target_entity_ids").GetArrayLength());
    }

    [TestMethod]
    public void An_automation_carries_the_position_its_location_device_was_paired_at()
    {
        var anchor = Switch(30);
        anchor.PairedX = 1500.5;
        anchor.PairedY = 820;
        var profile = new ServerProfile
        {
            Devices = new ObservableCollection<SmartDevice> { anchor, Switch(31) },
            IsDeviceAutomationActive = true,
            DeviceAutomationRules = new List<DeviceAutomationRule>
            {
                new() { Id = "a1", IsEnabled = true, LocationEntityId = 30, TargetEntityId = 31, SpecificPlayerSteamId = 76561198234471456 },
            },
        };

        var automations = Payload(profile).GetProperty("automations");
        var location = automations.GetProperty("device_locations").GetProperty("30");

        Assert.IsTrue(automations.GetProperty("enabled").GetBoolean());
        Assert.AreEqual(1500.5, location.GetProperty("x").GetDouble());
        Assert.AreEqual(820, location.GetProperty("y").GetDouble());
        // A steam id does not fit a JavaScript number, so it travels as a string.
        Assert.AreEqual("76561198234471456", automations.GetProperty("rules")[0].GetProperty("specific_player_steam_id").GetString());
    }

    [TestMethod]
    public void A_deleted_rule_is_simply_absent_from_the_next_push()
    {
        var profile = new ServerProfile
        {
            LogicRules = new List<LogicRule> { new() { Id = "keep" }, new() { Id = "gone" } },
        };
        profile.LogicRules.RemoveAll(r => r.Id == "gone");

        var ids = Payload(profile).GetProperty("logic").GetProperty("rules").EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()).ToArray();

        CollectionAssert.AreEqual(new[] { "keep" }, ids);
    }
}
