using System;
using System.Linq;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    /// <summary>
    /// Chat command answers on a server whose events come from audio detection.
    ///
    /// These are deliberately terser than the API-sourced replies. Everything the old answers
    /// carried beyond a timestamp — harbour names, docking countdowns, crate unlock timers,
    /// which rig was triggered — came from map markers. Inventing a plausible-sounding
    /// substitute would be worse than admitting the app only knows when something was heard.
    /// </summary>
    private string BuildCloudCargoAnswer()
    {
        var cargo = CloudEventWatcher.Instance.Get(RustEventKind.Cargo);
        if (cargo == null) return Properties.Resources.ChatCmdCargoCloudUnknown;

        return cargo.IsActive
            ? string.Format(Properties.Resources.ChatCmdCargoCloudActive, FormatAgo(cargo.Age))
            : string.Format(Properties.Resources.ChatCmdCargoCloudGone,
                            FormatAgo(DateTime.UtcNow - cargo.ExpiresAtUtc));
    }

    private string BuildCloudDeepSeaAnswer()
    {
        var deepSea = CloudEventWatcher.Instance.Get(RustEventKind.DeepSea);
        if (deepSea == null) return Properties.Resources.ChatCmdDeepSeaCloudUnknown;

        // Deep Sea runs exactly three hours, so the remaining time is arithmetic on the
        // detected start rather than a guess.
        return deepSea.IsActive
            ? string.Format(Properties.Resources.ChatCmdDeepSeaCloudActive, FormatAgo(deepSea.Remaining))
            : string.Format(Properties.Resources.ChatCmdDeepSeaCloudEnded,
                            FormatAgo(DateTime.UtcNow - deepSea.ExpiresAtUtc));
    }

    private string BuildCloudOilRigAnswer()
    {
        // A hack timer started from an RF receiver beats the audio cue outright: it names the
        // rig, and it counts down to the unlock rather than reporting how long ago something
        // was heard. Only fall through when no rig has one running.
        string? hacked = BuildOilRigTimerAnswer();
        if (hacked != null) return hacked;

        var oilRig = CloudEventWatcher.Instance.Get(RustEventKind.OilRig);
        if (oilRig == null || oilRig.RecentUtc.Count == 0)
            return Properties.Resources.ChatCmdOilRigCloudNone;

        // Both rigs share one cue, so the two most recent detections are reported without
        // claiming which rig either belonged to.
        string times = string.Join(", ", oilRig.RecentUtc
            .Take(2)
            .Select(seen => FormatAgo(DateTime.UtcNow - seen)));

        return string.Format(Properties.Resources.ChatCmdOilRigCloudSeen, times);
    }
}
