using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RustPlusDesk.Models;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// The in-game settings commands, <c>!mute [15m]</c>, <c>!unmute</c> and
    /// <c>!device &lt;#n|word|name&gt; name|command &lt;value&gt;</c>, without the UI.
    ///
    /// The cloud answers the same words with the same rules (CloudChatCommandService::manage on
    /// the platform), so a team gets one behaviour whether the app or the cloud is listening.
    /// </summary>
    public static class ChatManageCommands
    {
        /// <summary>Words these commands own; no device may answer to one of them.</summary>
        public static readonly string[] Words = { "mute", "unmute", "device" };

        /// <summary>The longest timed mute; the platform clamps to the same.</summary>
        public static readonly TimeSpan MaxMute = TimeSpan.FromDays(7);

        public const int DeviceNameMax = 40;
        public const int DeviceCommandMax = 32;

        /// <summary>Built-in words the platform and the app answer to, whatever the profile says.</summary>
        private static readonly string[] BuiltIns =
        {
            "help", "afk", "promote", "list", "deaths", "base_codes", "devices", "switches", "commands", "tc", "upkeep",
            "storage", "storages", "decay", "decaying", "who", "controller", "bot", "online", "team", "upkeepdetail",
            "code", "base_code", "pop", "time", "cargo", "heli", "vendor", "oilrig", "deepsea",
        };

        /// <summary>
        /// A duration typed after <c>!mute</c>: "15m", "30s", "2h", "1d", or a bare number of minutes.
        /// Returns true with null for nothing typed (until unmuted), false for anything else.
        /// </summary>
        public static bool TryParseDuration(string? text, out TimeSpan? duration)
        {
            duration = null;
            text = (text ?? "").Trim().ToLowerInvariant();
            if (text.Length == 0) return true;

            var m = Regex.Match(text, @"^(\d{1,6})\s*(s|sec|secs|m|min|mins|h|hr|hrs|d|day|days)?$");
            if (!m.Success) return false;

            long amount = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (amount <= 0) return false;

            var unit = m.Groups[2].Success ? m.Groups[2].Value[0] : 'm';
            var span = unit switch
            {
                's' => TimeSpan.FromSeconds(amount),
                'h' => TimeSpan.FromHours(amount),
                'd' => TimeSpan.FromDays(amount),
                _ => TimeSpan.FromMinutes(amount),
            };
            duration = span > MaxMute ? MaxMute : span;
            return true;
        }

        /// <summary>"15m", "2h", "1d 3h": short enough for a chat line, matching the cloud's wording.</summary>
        public static string Describe(TimeSpan span)
        {
            if (span.TotalSeconds < 60) return $"{(int)Math.Max(1, span.TotalSeconds)}s";
            int minutes = (int)Math.Ceiling(span.TotalMinutes);
            int days = minutes / 1440, hours = minutes % 1440 / 60, mins = minutes % 60;
            var text = (days > 0 ? $"{days}d " : "") + (hours > 0 ? $"{hours}h " : "") + (mins > 0 ? $"{mins}m" : "");
            return text.Trim();
        }

        /// <summary>Muted right now: either open-ended, or not yet run out.</summary>
        public static bool IsMuteActive(bool muted, DateTime? untilUtc, DateTime nowUtc)
            => muted && (untilUtc == null || untilUtc.Value > nowUtc);

        /// <summary>The parts of <c>device &lt;ref&gt; name|command &lt;value&gt;</c>, or null when it is not that shape.</summary>
        public static (string Ref, bool IsName, string Value)? ParseDevice(string rest)
        {
            var m = Regex.Match(rest ?? "", @"^(\S+)\s+(name|rename|command|cmd)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var field = m.Groups[2].Value.ToLowerInvariant();
            return (m.Groups[1].Value, field is "name" or "rename", m.Groups[3].Value.Trim());
        }

        /// <summary>
        /// A device by its number in the device list (#1 is the first switch), its entity id, its
        /// command word, or its name or alias without spaces.
        /// </summary>
        public static SmartDevice? FindDevice(ServerProfile profile, string reference)
        {
            var all = profile.AllDevices;
            reference = (reference ?? "").Trim().ToLowerInvariant();

            var number = Regex.Match(reference, @"^#(\d{1,3})$");
            if (number.Success)
            {
                var switches = all.Where(d => (d.Kind ?? "").Contains("switch", StringComparison.OrdinalIgnoreCase)).ToList();
                int index = int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                return index >= 0 && index < switches.Count ? switches[index] : null;
            }

            var word = reference.TrimStart('!');
            static string Squash(string? s) => Regex.Replace(s ?? "", @"\s+", "").ToLowerInvariant();

            foreach (var d in all)
            {
                var bound = profile.SwitchCommandMappings.Concat(profile.UpkeepCommandMappings)
                    .FirstOrDefault(m => m.EntityId == d.EntityId)?.Command;
                if (d.EntityId.ToString(CultureInfo.InvariantCulture) == reference
                    || string.Equals(bound, word, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(d.Alias) && Squash(d.Alias) == reference)
                    || (!string.IsNullOrEmpty(d.Name) && Squash(d.Name) == reference))
                    return d;
            }

            return null;
        }

        /// <summary>A name as typed, cleaned; null when it is empty or too long.</summary>
        public static string? CleanName(string value)
        {
            var name = Regex.Replace(value ?? "", @"[\u0000-\u001F\u007F]+", "").Trim();
            return name.Length == 0 || name.Length > DeviceNameMax ? null : name;
        }

        /// <summary>Why a word cannot be this device's command, or null when it can.</summary>
        public static string? CommandWordProblem(ServerProfile profile, uint entityId, string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length > DeviceCommandMax || !Regex.IsMatch(word, @"^[^\s\d]\S*$"))
                return $"A command word needs 1 to {DeviceCommandMax} characters, no spaces, and must not start with a digit.";

            var configured = new[]
            {
                profile.CmdPop, profile.CmdTime, profile.CmdAfk, profile.CmdOnline, profile.CmdDeaths, profile.CmdPromote,
                profile.CmdList, profile.CmdUpkeepDetail, profile.CmdBaseCodes, profile.CmdOilRig, profile.CmdAi,
            }.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w!.Trim().TrimStart('!').ToLowerInvariant());

            if (Words.Concat(BuiltIns).Concat(configured).Contains(word))
                return $"\"{word}\" is already a command.";

            if (profile.SwitchCommandMappings.Concat(profile.UpkeepCommandMappings)
                .Any(m => m.EntityId != entityId && string.Equals(m.Command, word, StringComparison.OrdinalIgnoreCase)))
                return $"\"{word}\" already belongs to another device.";

            return null;
        }

        /// <summary>
        /// Bind a word to a device: the cupboard's upkeep binding if it has one, its switch
        /// binding otherwise, created when missing.
        /// </summary>
        public static void BindCommand(ServerProfile profile, SmartDevice device, string word)
        {
            var mapping = profile.SwitchCommandMappings.FirstOrDefault(m => m.EntityId == device.EntityId)
                ?? profile.UpkeepCommandMappings.FirstOrDefault(m => m.EntityId == device.EntityId);

            if (mapping == null)
            {
                profile.SwitchCommandMappings.Add(new ChatCommandMapping
                {
                    EntityId = device.EntityId,
                    Command = word,
                    Label = device.PureName,
                });
            }
            else
            {
                mapping.Command = word;
            }
        }
    }
}
