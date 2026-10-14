using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

/// <summary>
/// Exports Logic Engine rules to a portable JSON document and brings them back in safely.
///
/// A rule file can come from a teammate, so import treats it as untrusted input: every rule
/// gets a fresh ID and arrives disabled, links between imported rules are re-pointed at the
/// new IDs, and values that would break the runtime (null strings, negative waits) are
/// repaired. Device entity IDs are specific to a server, so the result reports how many of
/// them are unknown on the target server instead of guessing a mapping.
/// </summary>
public static class LogicRuleTransfer
{
    public const string FormatId = "rustplusdesk-logic-rules";
    public const int CurrentVersion = 1;
    public const int MaxFileBytes = 2 * 1024 * 1024;
    public const int MaxRules = 200;

    private const int MaxWaitSeconds = 24 * 60 * 60;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class Envelope
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public DateTime ExportedUtc { get; set; }
        public List<LogicRule?>? Rules { get; set; }
    }

    public sealed class ImportResult
    {
        public List<LogicRule> Rules { get; } = new();
        public string? Error { get; set; }
        public bool Success => Error == null;

        /// <summary>Distinct device entity IDs the rules refer to that the target server does not have.</summary>
        public int UnknownDeviceCount { get; set; }

        /// <summary>Rules triggered by another rule that was not part of the file; the link was cleared.</summary>
        public int BrokenRuleLinks { get; set; }
    }

    public static string Export(IEnumerable<LogicRule> rules)
    {
        var envelope = new Envelope
        {
            Format = FormatId,
            Version = CurrentVersion,
            ExportedUtc = DateTime.UtcNow,
            Rules = rules.Cast<LogicRule?>().ToList(),
        };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static ImportResult Import(
        string? json,
        IEnumerable<SmartDevice>? knownDevices,
        IEnumerable<LogicRule>? existingRules)
    {
        var result = new ImportResult();

        if (string.IsNullOrWhiteSpace(json))
        {
            result.Error = "The file is empty.";
            return result;
        }

        if (json.Length > MaxFileBytes)
        {
            result.Error = "The file is too large to be a rule export.";
            return result;
        }

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, JsonOptions);
        }
        catch (JsonException)
        {
            result.Error = "The file is not valid JSON.";
            return result;
        }

        if (envelope == null || envelope.Format != FormatId)
        {
            result.Error = "This file is not a Logic Engine rule export.";
            return result;
        }

        if (envelope.Version > CurrentVersion)
        {
            result.Error = "This file was exported by a newer version of the app. Update the app and try again.";
            return result;
        }

        var incoming = envelope.Rules?.Where(r => r != null).Select(r => r!).ToList();
        if (incoming == null || incoming.Count == 0)
        {
            result.Error = "The file does not contain any rules.";
            return result;
        }

        if (incoming.Count > MaxRules)
        {
            result.Error = $"The file contains more than {MaxRules} rules.";
            return result;
        }

        // Fresh IDs first, so links between imported rules can be re-pointed in the second pass.
        var idMap = new Dictionary<string, string>();
        foreach (var rule in incoming)
        {
            var newId = Guid.NewGuid().ToString();
            if (!string.IsNullOrEmpty(rule.Id))
                idMap.TryAdd(rule.Id, newId);
            rule.Id = newId;
        }

        var existingNames = new HashSet<string>(
            (existingRules ?? Enumerable.Empty<LogicRule>()).Select(r => r.Name ?? ""),
            StringComparer.OrdinalIgnoreCase);

        var known = knownDevices == null
            ? null
            : new HashSet<uint>(knownDevices.Select(d => d.EntityId));
        var unknown = new HashSet<uint>();

        foreach (var rule in incoming)
        {
            Normalize(rule);

            if (rule.TriggerType is "RuleTriggered" or "RuleCompleted")
            {
                if (idMap.TryGetValue(rule.TriggerRuleId, out var mapped))
                {
                    rule.TriggerRuleId = mapped;
                }
                else
                {
                    if (!string.IsNullOrEmpty(rule.TriggerRuleId)) result.BrokenRuleLinks++;
                    rule.TriggerRuleId = "";
                }
            }
            else
            {
                rule.TriggerRuleId = "";
            }

            if (existingNames.Contains(rule.Name))
                rule.Name += " (imported)";
            existingNames.Add(rule.Name);

            if (known != null) CollectUnknownDevices(rule, known, unknown);

            result.Rules.Add(rule);
        }

        result.UnknownDeviceCount = unknown.Count;
        return result;
    }

    private static void Normalize(LogicRule rule)
    {
        // Nothing arrives running: the user reviews a rule before it can act on a base.
        rule.IsEnabled = false;
        rule.IsExpanded = false;
        rule.IsConfirmingDelete = false;

        rule.Name = string.IsNullOrWhiteSpace(rule.Name) ? "Imported rule" : rule.Name;
        rule.TriggerType ??= "SmartAlarm";
        rule.TriggerCommand ??= "rulecommand";
        rule.TriggerRuleId ??= "";
        rule.ConditionOperator ??= "NONE";
        rule.Steps ??= new();
        foreach (var step in rule.Steps.Where(s => s != null).ToList())
            NormalizeStep(step);
    }

    private static void NormalizeStep(LogicStep step)
    {
        step.StepType ??= "Wait";
        step.ConditionOperator ??= "ALL_OFFLINE";
        step.ConditionDeviceIdsCsv ??= "";
        step.TargetGroupName ??= "";
        step.TimerTarget ??= "Custom";
        step.TimerName ??= "";

        // A negative wait would throw in Task.Delay, and -1 second is not the same as -1 ms there.
        step.WaitSeconds = Math.Clamp(step.WaitSeconds, 0, MaxWaitSeconds);

        step.ConditionalSteps ??= new();
        foreach (var nested in step.ConditionalSteps.Where(s => s != null).ToList())
            NormalizeStep(nested);
    }

    private static void CollectUnknownDevices(LogicRule rule, HashSet<uint> known, HashSet<uint> unknown)
    {
        void Check(uint id)
        {
            if (id != 0 && !known.Contains(id)) unknown.Add(id);
        }

        if (rule.TriggerType is "SmartAlarm" or "SmartSwitch") Check(rule.TriggerEntityId);
        if (rule.ConditionOperator != "NONE") Check(rule.ConditionDeviceEntityId);

        void Walk(IEnumerable<LogicStep> steps)
        {
            foreach (var step in steps)
            {
                if (step == null) continue;
                if (step.StepType == "Toggle") Check(step.TargetEntityId);

                foreach (var part in step.ConditionDeviceIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (uint.TryParse(part, out var id)) Check(id);
                }

                Walk(step.ConditionalSteps);
            }
        }

        Walk(rule.Steps);
    }
}
