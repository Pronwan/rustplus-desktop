using System.Collections.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class LogicRuleTransferTests
{
    private static LogicRule Rule(string id, string name, string trigger = "SmartAlarm") => new()
    {
        Id = id,
        Name = name,
        TriggerType = trigger,
        IsEnabled = true,
        IsExpanded = true,
    };

    [TestMethod]
    public void RoundTripKeepsStepsAndNestedSteps()
    {
        var rule = Rule("a", "Raid response");
        rule.Steps.Add(new LogicStep
        {
            StepType = "CheckAvailability",
            ConditionOperator = "ANY_ONLINE",
            ConditionalSteps = new ObservableCollection<LogicStep>
            {
                new LogicStep { StepType = "Toggle", TargetEntityId = 42, ToggleState = true },
            },
        });
        rule.Steps.Add(new LogicStep { StepType = "Wait", WaitSeconds = 30 });

        var json = LogicRuleTransfer.Export(new[] { rule });
        var result = LogicRuleTransfer.Import(json, null, null);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(1, result.Rules.Count);
        var imported = result.Rules[0];
        Assert.AreEqual("Raid response", imported.Name);
        Assert.AreEqual(2, imported.Steps.Count);
        Assert.AreEqual("ANY_ONLINE", imported.Steps[0].ConditionOperator);
        Assert.AreEqual(1, imported.Steps[0].ConditionalSteps.Count);
        Assert.AreEqual(42u, imported.Steps[0].ConditionalSteps[0].TargetEntityId);
        Assert.AreEqual(30, imported.Steps[1].WaitSeconds);
    }

    [TestMethod]
    public void ImportedRulesGetNewIdsAndStartDisabled()
    {
        var json = LogicRuleTransfer.Export(new[] { Rule("old-id", "Rule") });
        var result = LogicRuleTransfer.Import(json, null, null);

        var imported = result.Rules.Single();
        Assert.AreNotEqual("old-id", imported.Id);
        Assert.IsFalse(imported.IsEnabled);
        Assert.IsFalse(imported.IsExpanded);
    }

    [TestMethod]
    public void RuleTriggerLinksFollowTheNewIds()
    {
        var first = Rule("first", "First");
        var second = Rule("second", "Second", "RuleCompleted");
        second.TriggerRuleId = "first";

        var json = LogicRuleTransfer.Export(new[] { first, second });
        var result = LogicRuleTransfer.Import(json, null, null);

        Assert.AreEqual(result.Rules[0].Id, result.Rules[1].TriggerRuleId);
        Assert.AreEqual(0, result.BrokenRuleLinks);
    }

    [TestMethod]
    public void LinkToARuleOutsideTheFileIsClearedAndCounted()
    {
        var orphan = Rule("orphan", "Orphan", "RuleTriggered");
        orphan.TriggerRuleId = "not-in-file";

        var json = LogicRuleTransfer.Export(new[] { orphan });
        var result = LogicRuleTransfer.Import(json, null, null);

        Assert.AreEqual("", result.Rules.Single().TriggerRuleId);
        Assert.AreEqual(1, result.BrokenRuleLinks);
    }

    [TestMethod]
    public void NameCollisionsWithExistingRulesAreMarked()
    {
        var json = LogicRuleTransfer.Export(new[] { Rule("a", "Defence") });
        var result = LogicRuleTransfer.Import(json, null, new[] { Rule("x", "defence") });

        Assert.AreEqual("Defence (imported)", result.Rules.Single().Name);
    }

    [TestMethod]
    public void UnknownDevicesAreCountedOncePerId()
    {
        var rule = Rule("a", "Rule");
        rule.TriggerEntityId = 100;
        rule.Steps.Add(new LogicStep { StepType = "Toggle", TargetEntityId = 200 });
        rule.Steps.Add(new LogicStep { StepType = "Toggle", TargetEntityId = 200 });
        rule.Steps.Add(new LogicStep { StepType = "CheckAvailability", ConditionDeviceIdsCsv = "100, 300" });

        var json = LogicRuleTransfer.Export(new[] { rule });
        var devices = new[] { new SmartDevice { EntityId = 100 } };
        var result = LogicRuleTransfer.Import(json, devices, null);

        Assert.AreEqual(2, result.UnknownDeviceCount); // 200 and 300; 100 exists
    }

    [TestMethod]
    public void NegativeWaitIsClampedBecauseTaskDelayWouldThrow()
    {
        var rule = Rule("a", "Rule");
        rule.Steps.Add(new LogicStep { StepType = "Wait", WaitSeconds = -5 });
        rule.Steps.Add(new LogicStep { StepType = "Wait", WaitSeconds = int.MaxValue });

        var result = LogicRuleTransfer.Import(LogicRuleTransfer.Export(new[] { rule }), null, null);

        Assert.AreEqual(0, result.Rules[0].Steps[0].WaitSeconds);
        Assert.AreEqual(24 * 60 * 60, result.Rules[0].Steps[1].WaitSeconds);
    }

    [TestMethod]
    public void NullFieldsInTheFileAreRepaired()
    {
        const string json = """
        { "format": "rustplusdesk-logic-rules", "version": 1,
          "rules": [ { "id": "a", "name": null, "triggerType": null, "steps": null } ] }
        """;

        var result = LogicRuleTransfer.Import(json, null, null);

        Assert.IsTrue(result.Success, result.Error);
        var rule = result.Rules.Single();
        Assert.AreEqual("Imported rule", rule.Name);
        Assert.AreEqual("SmartAlarm", rule.TriggerType);
        Assert.IsNotNull(rule.Steps);
    }

    [TestMethod]
    public void RejectsFilesThatAreNotRuleExports()
    {
        Assert.IsFalse(LogicRuleTransfer.Import("", null, null).Success);
        Assert.IsFalse(LogicRuleTransfer.Import("not json", null, null).Success);
        Assert.IsFalse(LogicRuleTransfer.Import("[]", null, null).Success);
        Assert.IsFalse(LogicRuleTransfer.Import("{\"format\":\"something-else\",\"version\":1,\"rules\":[{}]}", null, null).Success);
    }

    [TestMethod]
    public void RejectsNewerFormatVersionsAndEmptyFiles()
    {
        var newer = $"{{\"format\":\"{LogicRuleTransfer.FormatId}\",\"version\":{LogicRuleTransfer.CurrentVersion + 1},\"rules\":[{{}}]}}";
        var empty = $"{{\"format\":\"{LogicRuleTransfer.FormatId}\",\"version\":1,\"rules\":[]}}";

        Assert.IsFalse(LogicRuleTransfer.Import(newer, null, null).Success);
        Assert.IsFalse(LogicRuleTransfer.Import(empty, null, null).Success);
    }
}
