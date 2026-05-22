using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Views;

namespace RustPlusDesk.Modification
{
    public class SmartSwitchesMod : IMod
    {
        public string Id => "SmartSwitches";
        public string Name => "Smart Switches";
        public string Description => "Enables advanced timer-based and chat-based controls for Rust smart switches. " +
                                     "Rename your switch to 'timer(300)[off]' to turn it off 300 seconds after it is turned on. " +
                                     "Or rename it to 'chat(!Turret)' to toggle it (ON <-> OFF) whenever that string is written in team chat.";
        
        public bool IsEnabled { get; set; } = true;

        private MainWindow? _mainWindow;
        
        // Thread-safe dictionary to keep track of active timer tasks for cancellation
        private readonly ConcurrentDictionary<uint, CancellationTokenSource> _timerCancellationTokens = new();

        // Regex helpers
        private static readonly Regex TimerRegex = new Regex(@"timer\s*\(\s*(\d+)\s*\)\s*\[\s*off\s*\]", RegexOptions.IgnoreCase);
        private static readonly Regex ChatRegex = new Regex(@"chat\s*\(\s*([^)]+)\s*\)", RegexOptions.IgnoreCase);

        public void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void OnChatReceived(string text, string author, ulong steamId)
        {
            if (_mainWindow == null || string.IsNullOrWhiteSpace(text)) return;

            var devices = _mainWindow.ModGetDevices();
            if (devices == null) return;

            var messageText = text.Trim();

            // Find all smart switches matching chat(trigger)
            foreach (var dev in FlattenDevices(devices))
            {
                if (dev.IsGroup || string.IsNullOrWhiteSpace(dev.Name)) continue;

                // Verify it's a smart switch
                bool isSmartSwitch = string.Equals(dev.Kind, "SmartSwitch", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(dev.Kind, "Smart Switch", StringComparison.OrdinalIgnoreCase);

                if (!isSmartSwitch) continue;

                var chatMatch = ChatRegex.Match(dev.Name);
                if (chatMatch.Success)
                {
                    var trigger = chatMatch.Groups[1].Value.Trim();

                    // Compare trigger (case-insensitive, trimmed)
                    if (string.Equals(messageText, trigger, StringComparison.OrdinalIgnoreCase))
                    {
                        // Toggle logic: if IsOn is true, turn OFF, otherwise turn ON
                        bool targetState = !(dev.IsOn == true);
                        _mainWindow.ModLog($"Chat triggered toggle for switch '{dev.Name}' (#{dev.EntityId}) by '{author}': new state = {(targetState ? "ON" : "OFF")}");
                        
                        // Fire-and-forget toggle call
                        _ = _mainWindow.ModToggleSwitchAsync(dev.EntityId, targetState);
                    }
                }
            }
        }

        public void OnDeviceStateChanged(uint entityId, bool isOn, string kind)
        {
            if (_mainWindow == null) return;

            var devices = _mainWindow.ModGetDevices();
            if (devices == null) return;

            // Find the device
            var dev = FindDeviceById(devices, entityId);
            if (dev == null || string.IsNullOrWhiteSpace(dev.Name)) return;

            // If the switch turns OFF, cancel any pending turn-off timers
            if (!isOn)
            {
                if (_timerCancellationTokens.TryRemove(entityId, out var cts))
                {
                    _mainWindow.ModLog($"Switch '{dev.Name}' (#{entityId}) turned OFF. Cancelling pending auto-off timer.");
                    try { cts.Cancel(); } catch { }
                    cts.Dispose();
                }
                return;
            }

            // If the switch turns ON, check if it has a timer(X)[off] rule
            var timerMatch = TimerRegex.Match(dev.Name);
            if (timerMatch.Success)
            {
                if (int.TryParse(timerMatch.Groups[1].Value, out int seconds) && seconds > 0)
                {
                    // Cancel any previous timer for the same switch
                    if (_timerCancellationTokens.TryRemove(entityId, out var oldCts))
                    {
                        try { oldCts.Cancel(); } catch { }
                        oldCts.Dispose();
                    }

                    var newCts = new CancellationTokenSource();
                    _timerCancellationTokens[entityId] = newCts;

                    _mainWindow.ModLog($"Switch '{dev.Name}' (#{entityId}) turned ON. Scheduling auto-off in {seconds} seconds.");

                    // Start background task to turn it off
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(seconds), newCts.Token);
                            
                            if (!newCts.Token.IsCancellationRequested)
                            {
                                _mainWindow.ModLog($"Timer expired for switch '{dev.Name}' (#{entityId}). Turning OFF.");
                                await _mainWindow.ModToggleSwitchAsync(entityId, false);
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            // Expected cancellation
                        }
                        catch (Exception ex)
                        {
                            _mainWindow.ModLog($"Error in timer task for switch #{entityId}: {ex.Message}");
                        }
                        finally
                        {
                            // Clean up dictionary if this CTS is still the active one
                            if (_timerCancellationTokens.TryGetValue(entityId, out var activeCts) && activeCts == newCts)
                            {
                                _timerCancellationTokens.TryRemove(entityId, out _);
                            }
                            newCts.Dispose();
                        }
                    });
                }
            }
        }

        // Helper to flatten hierarchical device lists (handling groups)
        private System.Collections.Generic.IEnumerable<SmartDevice> FlattenDevices(System.Collections.IEnumerable devices)
        {
            foreach (var item in devices)
            {
                if (item is SmartDevice dev)
                {
                    yield return dev;
                    if (dev.IsGroup && dev.Children != null)
                    {
                        foreach (var child in FlattenDevices(dev.Children))
                        {
                            yield return child;
                        }
                    }
                }
            }
        }

        // Helper to find a specific device by its Entity ID
        private SmartDevice? FindDeviceById(System.Collections.IEnumerable devices, uint entityId)
        {
            return FlattenDevices(devices).FirstOrDefault(d => d.EntityId == entityId);
        }

        public System.Windows.FrameworkElement? GetConfigUI()
        {
            return null;
        }
    }
}
