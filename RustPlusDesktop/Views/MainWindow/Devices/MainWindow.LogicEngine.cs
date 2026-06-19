using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class MainWindow
    {
        // Tracks if a rule is currently executing an API call (to lock manual interventions)
        private bool _logicEngineRunningAction = false;
        
        // Mutex/Semaphore to ensure only one rule run runs at a time (strictly sequential)
        private readonly SemaphoreSlim _logicEngineSemaphore = new SemaphoreSlim(1, 1);

        // Public helper to check if Logic Engine is active and waiting
        public bool IsLogicEngineActiveAndWaiting
        {
            get
            {
                var profile = _vm?.Selected;
                if (profile == null || !profile.IsLogicEngineActive) return false;
                return true;
            }
        }

        // Stop the currently running Logic Engine rule
        public void StopLogicEngineExecution()
        {
            LogicEngineRuntimeService.Instance.RequestStop();
            AppendLog("[LogicEngine] Stop requested. Current rule will abort after the current operation.");
        }

        // Trigger hooks for FCM/WebSocket device updates
        public void TriggerLogicEngineOnDeviceEvent(uint entityId, bool isOn)
        {
            if (!IsLogicEngineActiveAndWaiting) return;

            var profile = _vm?.Selected;
            if (profile == null || profile.LogicRules == null) return;

            foreach (var rule in profile.LogicRules)
            {
                if (!rule.IsEnabled) continue;

                // Match trigger type
                if (rule.TriggerType == "SmartAlarm" || rule.TriggerType == "SmartSwitch")
                {
                    if (rule.TriggerEntityId == entityId && rule.TriggerState == isOn)
                    {
                        // Check trigger conditions (AND / OR)
                        if (EvaluateTriggerCondition(rule))
                        {
                            _ = EnqueueRuleExecutionAsync(rule);
                        }
                    }
                }
            }
        }

        // Trigger hooks for Chat Commands
        public void TriggerLogicEngineOnChatCommand(string cmdText)
        {
            if (!IsLogicEngineActiveAndWaiting) return;
            if (_chatFeaturesBlockedByMaster) return;

            var profile = _vm?.Selected;
            if (profile == null || profile.LogicRules == null) return;

            var prefix = profile.ChatCommandPrefix ?? "!";
            var cmd = cmdText.Trim().ToLowerInvariant();
            if (cmd.StartsWith(prefix))
            {
                cmd = cmd.Substring(prefix.Length).Trim();
            }

            foreach (var rule in profile.LogicRules)
            {
                if (!rule.IsEnabled || rule.TriggerType != "ChatCommand") continue;

                var ruleCmd = rule.TriggerCommand?.Trim().ToLowerInvariant() ?? "";
                if (ruleCmd.StartsWith(prefix))
                {
                    ruleCmd = ruleCmd.Substring(prefix.Length).Trim();
                }

                if (ruleCmd == cmd)
                {
                    if (EvaluateTriggerCondition(rule))
                    {
                        _ = EnqueueRuleExecutionAsync(rule);
                    }
                }
            }
        }

        private bool EvaluateTriggerCondition(LogicRule rule)
        {
            if (rule.ConditionOperator == "NONE" || rule.ConditionOperator == null)
            {
                return true;
            }

            var profile = _vm?.Selected;
            if (profile == null) return false;

            var condDev = FindDeviceById(profile.Devices, rule.ConditionDeviceEntityId);
            if (condDev == null)
            {
                // Condition device deleted / missing, condition cannot be met
                return false;
            }

            bool condState = condDev.IsOn ?? false;
            bool targetState = rule.ConditionDeviceState;

            if (rule.ConditionOperator == "AND")
            {
                return condState == targetState;
            }
            else if (rule.ConditionOperator == "OR")
            {
                // For trigger + OR condition, either the main trigger event happened OR condition is true.
                // In typical triggers, since the event just occurred, OR makes it trigger anyway.
                return true; 
            }

            return true;
        }

        private async Task EnqueueRuleExecutionAsync(LogicRule rule)
        {
            var runtime = LogicEngineRuntimeService.Instance;
            AppendLog($"[LogicEngine] Rule '{rule.Name}' triggered. Enqueuing...");

            // Track the rule in the pending queue so the UI can show what is waiting
            runtime.PendingRules.Add(rule.Name);

            // Wait to execute sequentially
            await _logicEngineSemaphore.WaitAsync();
            try
            {
                runtime.PendingRules.Remove(rule.Name);

                if (_chatFeaturesBlockedByMaster && rule.TriggerType == "ChatCommand")
                {
                    AppendLog($"[LogicEngine] Rule '{rule.Name}' execution aborted: Blocked by active Chat Master: {_chatFeatureMasterName}");
                    return;
                }

                using var cts = new CancellationTokenSource();
                runtime.CurrentCancellation = cts;
                runtime.IsRunning = true;
                runtime.CurrentRuleName = rule.Name;
                runtime.CurrentStepNumber = 0;
                runtime.CurrentStepType = null;

                try
                {
                    await RunRuleStepsAsync(rule, cts.Token);
                }
                finally
                {
                    runtime.IsRunning = false;
                    runtime.CurrentRuleName = null;
                    runtime.CurrentStepNumber = 0;
                    runtime.CurrentStepType = null;
                    runtime.CurrentCancellation = null;
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog($"[LogicEngine] Rule '{rule.Name}' execution was stopped.");
            }
            catch (Exception ex)
            {
                AppendLog($"[LogicEngine] Rule '{rule.Name}' error: {ex.Message}");
                await HandleRuleFailureAsync(rule, $"Rule '{rule.Name}' failed: {ex.Message}");
            }
            finally
            {
                _logicEngineSemaphore.Release();
            }
        }

        private async Task RunRuleStepsAsync(LogicRule rule, CancellationToken cancellationToken)
        {
            var runtime = LogicEngineRuntimeService.Instance;
            AppendLog($"[LogicEngine] Starting execution of rule '{rule.Name}'...");
            int stepNum = 0;
            foreach (var step in rule.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stepNum++;
                runtime.CurrentStepNumber = stepNum;
                runtime.CurrentStepType = step.StepType;
                AppendLog($"[LogicEngine] Running step {stepNum} ({step.StepType}) for rule '{rule.Name}'...");

                // Strict Cooldown/Mutex check: if manual action is already running, wait.
                while (_globalToggleBusy || _refreshAllBusy == 1)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(500, cancellationToken);
                }

                if (step.StepType == "Wait")
                {
                    await Task.Delay(step.WaitSeconds * 1000, cancellationToken);
                }
                else if (step.StepType == "Toggle")
                {
                    await ExecuteToggleStepAsync(step, cancellationToken);
                }
                else if (step.StepType == "CheckAvailability")
                {
                    bool conditionMet = await ExecuteCheckAvailabilityStepAsync(step, rule, cancellationToken);
                    if (!conditionMet)
                    {
                        AppendLog($"[LogicEngine] Gating condition failed for rule '{rule.Name}'. Aborting rule execution.");
                        break;
                    }
                }
            }
            AppendLog($"[LogicEngine] Completed execution of rule '{rule.Name}'.");
        }

        private async Task ExecuteToggleStepAsync(LogicStep step, CancellationToken cancellationToken)
        {
            var profile = _vm?.Selected;
            if (profile == null) return;

            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(step.TargetGroupName))
            {
                // Target is a group
                var groupDev = profile.Devices.FirstOrDefault(d => d.IsGroup && d.Alias == step.TargetGroupName);
                if (groupDev == null || groupDev.Children == null)
                {
                    throw new Exception($"Group '{step.TargetGroupName}' not found or has no devices.");
                }

                var switches = GetSwitchesRecursive(groupDev.Children);
                if (!switches.Any()) return;

                if (!await EnsureConnectedAsync()) throw new Exception("Companion app not connected.");

                // If step.ToggleState is specified, force that. Otherwise invert based on first device.
                bool targetOn = step.ToggleState ?? !switches.First().IsOn.GetValueOrDefault(false);

                // Set Action Mutex
                _logicEngineRunningAction = true;
                try
                {
                    foreach (var sw in switches)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (sw.IsOn == targetOn) continue;

                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        cts.CancelAfter(TimeSpan.FromSeconds(5));
                        await _rust.ToggleSmartSwitchAsync(sw.EntityId, targetOn, cts.Token);
                        sw.IsOn = targetOn;
                        await Task.Delay(800, cancellationToken); // Wait between toggle calls
                    }
                }
                finally
                {
                    _logicEngineRunningAction = false;
                }
            }
            else
            {
                // Target is single switch
                var dev = FindDeviceById(profile.Devices, step.TargetEntityId);
                if (dev == null) throw new Exception($"Target switch #{step.TargetEntityId} not found.");
                if (dev.IsMissing) throw new Exception($"Target switch #{step.TargetEntityId} is offline/missing.");

                if (!await EnsureConnectedAsync()) throw new Exception("Companion app not connected.");

                bool targetOn = step.ToggleState ?? !(dev.IsOn ?? false);

                // Set Action Mutex
                _logicEngineRunningAction = true;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    await _rust.ToggleSmartSwitchAsync(dev.EntityId, targetOn, cts.Token);
                    dev.IsOn = targetOn;
                    await Task.Delay(800, cancellationToken);
                }
                finally
                {
                    _logicEngineRunningAction = false;
                }
            }
        }

        private async Task<bool> ExecuteCheckAvailabilityStepAsync(LogicStep step, LogicRule rule, CancellationToken cancellationToken)
        {
            var profile = _vm?.Selected;
            if (profile == null) return false;

            cancellationToken.ThrowIfCancellationRequested();

            // Wait until manual refresh finishes if busy, then run a single refresh
            while (_refreshAllBusy == 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(500, cancellationToken);
            }

            // Set Action Mutex
            _logicEngineRunningAction = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendLog("[LogicEngine] Refreshing device availability states...");
                await RefreshAllDevicesStatusAsync();
            }
            finally
            {
                _logicEngineRunningAction = false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            bool conditionMet = false;

            if (step.TargetEntityId != 0)
            {
                var dev = FindDeviceById(profile.Devices, step.TargetEntityId);
                bool isOffline = (dev == null || dev.IsMissing);

                if (step.ConditionOperator == "IS_OFFLINE" || step.ConditionOperator == "ALL_OFFLINE" || step.ConditionOperator == "ANY_OFFLINE")
                {
                    conditionMet = isOffline;
                }
                else if (step.ConditionOperator == "IS_ONLINE" || step.ConditionOperator == "ALL_ONLINE" || step.ConditionOperator == "ANY_ONLINE")
                {
                    conditionMet = !isOffline;
                }
            }
            else
            {
                var deviceIds = step.ConditionDeviceIdsCsv
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => uint.TryParse(s.Trim(), out var id) ? id : 0)
                    .Where(id => id != 0)
                    .ToList();

                if (!deviceIds.Any()) return false;

                var offlineCount = 0;
                var onlineCount = 0;

                foreach (var id in deviceIds)
                {
                    var dev = FindDeviceById(profile.Devices, id);
                    if (dev == null || dev.IsMissing)
                    {
                        offlineCount++;
                    }
                    else
                    {
                        onlineCount++;
                    }
                }

                if (step.ConditionOperator == "ALL_OFFLINE")
                {
                    conditionMet = offlineCount == deviceIds.Count;
                }
                else if (step.ConditionOperator == "ANY_OFFLINE")
                {
                    conditionMet = offlineCount > 0;
                }
                else if (step.ConditionOperator == "ALL_ONLINE")
                {
                    conditionMet = onlineCount == deviceIds.Count;
                }
                else if (step.ConditionOperator == "ANY_ONLINE")
                {
                    conditionMet = onlineCount > 0;
                }
            }

            if (conditionMet)
            {
                AppendLog($"[LogicEngine] Availability condition '{step.ConditionOperator}' met. Executing conditional steps...");
                foreach (var condStep in step.ConditionalSteps)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    while (_globalToggleBusy || _refreshAllBusy == 1)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Delay(500, cancellationToken);
                    }

                    if (condStep.StepType == "Wait")
                    {
                        await Task.Delay(condStep.WaitSeconds * 1000, cancellationToken);
                    }
                    else if (condStep.StepType == "Toggle")
                    {
                        await ExecuteToggleStepAsync(condStep, cancellationToken);
                    }
                }
            }
            return conditionMet;
        }

        private async Task HandleRuleFailureAsync(LogicRule rule, string errorMsg)
        {
            var profile = _vm?.Selected;
            if (profile == null) return;

            // Chat Alert
            await Dispatcher.InvokeAsync(() =>
            {
                AddIncomingChatMessage("LogicEngine", $"⚠️ {errorMsg}");
            });

            // Discord Event Channel Alert
            if (RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium)
            {
                try
                {
                    await DiscordBotListenerService.Instance.SendNotificationAsync("event", $"[LogicEngine] {errorMsg}");
                }
                catch (Exception ex)
                {
                    AppendLog($"[LogicEngine] Failed to send Discord error notification: {ex.Message}");
                }
            }
        }
    }
}
