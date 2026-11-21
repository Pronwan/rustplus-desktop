using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services;

internal sealed class DeviceGroupToggle
{
    internal sealed record Result(int Changed, int Unchanged, int Skipped, int Failed, int Interrupted);

    private readonly HashSet<SmartDevice> _runningGroups = new(ReferenceEqualityComparer.Instance);

    // Called on the UI dispatcher; state is deliberately read again between awaits.
    internal async Task<Result?> RunAsync(
        SmartDevice group,
        Func<SmartDevice, bool> isBusy,
        Func<SmartDevice, bool, Task<bool>> tryToggleAsync,
        Func<bool> isCurrent,
        Func<Task<bool>>? ensureConnectedAsync = null,
        Action<SmartDevice, Exception>? onFailure = null)
    {
        if (!_runningGroups.Add(group)) return null;

        try
        {
            bool CannotToggle(SmartDevice device) => device.IsMissing || device.IsToggleBusy || isBusy(device);

            var switches = GetSwitches(group.Children).DistinctBy(device => device.EntityId).ToArray();
            var eligible = switches.Where(device => !CannotToggle(device)).ToArray();
            int skipped = switches.Length - eligible.Length;
            if (eligible.Length == 0) return new(0, 0, skipped, 0, 0);

            // Only turn the group off when every eligible switch is known to be on.
            bool targetOn = eligible.Any(device => device.IsOn != true);
            if (!isCurrent() || (ensureConnectedAsync != null && !await ensureConnectedAsync()))
                return new(0, 0, skipped, 0, eligible.Length);

            int changed = 0, unchanged = 0, failed = 0;
            for (int i = 0; i < eligible.Length; i++)
            {
                if (!isCurrent()) return new(changed, unchanged, skipped, failed, eligible.Length - i);

                var device = eligible[i];
                if (CannotToggle(device)) skipped++;
                else if (device.IsOn == targetOn) unchanged++;
                else
                {
                    try
                    {
                        if (await tryToggleAsync(device, targetOn)) changed++;
                        else if (!isCurrent()) return new(changed, unchanged, skipped, failed, eligible.Length - i);
                        else skipped++;
                    }
                    catch (Exception error)
                    {
                        if (!isCurrent()) return new(changed, unchanged, skipped, failed, eligible.Length - i);
                        failed++;
                        onFailure?.Invoke(device, error);
                    }
                }
            }
            return new(changed, unchanged, skipped, failed, 0);
        }
        finally
        {
            _runningGroups.Remove(group);
        }
    }

    internal static IEnumerable<SmartDevice> GetSwitches(IEnumerable<SmartDevice>? devices)
    {
        if (devices == null) yield break;
        foreach (var device in devices)
        {
            if (device.IsGroup)
            {
                foreach (var child in GetSwitches(device.Children)) yield return child;
            }
            else if (string.Equals(device.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(device.Kind, "Smart Switch", StringComparison.OrdinalIgnoreCase))
            {
                yield return device;
            }
        }
    }
}
