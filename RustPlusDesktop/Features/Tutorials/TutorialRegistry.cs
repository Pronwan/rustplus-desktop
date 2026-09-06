using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Views;

namespace RustPlusDesk.Features.Tutorials;

public interface ITutorialRegistry
{
    IReadOnlyList<TutorialDefinition> Tutorials { get; }
    TutorialDefinition? Find(string tutorialId);
    IReadOnlyList<string> Validate(Func<string, bool>? localizationKeyExists = null);
}

public sealed class TutorialRegistry : ITutorialRegistry
{
    public TutorialRegistry() => Tutorials = CreateDefinitions();

    public IReadOnlyList<TutorialDefinition> Tutorials { get; }
    public TutorialDefinition? Find(string tutorialId) => Tutorials.FirstOrDefault(x => x.Id == tutorialId);

    public IReadOnlyList<string> Validate(Func<string, bool>? localizationKeyExists = null)
    {
        var errors = new List<string>();
        foreach (var duplicate in Tutorials.GroupBy(x => x.Id).Where(x => x.Count() > 1))
            errors.Add($"Duplicate tutorial ID: {duplicate.Key}");
        foreach (var duplicate in Tutorials.GroupBy(x => x.DisplayOrder).Where(x => x.Count() > 1))
            errors.Add($"Duplicate tutorial order: {duplicate.Key}");

        foreach (var tutorial in Tutorials)
        {
            if (tutorial.Steps.Count == 0) errors.Add($"{tutorial.Id} has no steps.");
            foreach (var duplicate in tutorial.Steps.GroupBy(x => x.Id).Where(x => x.Count() > 1))
                errors.Add($"Duplicate step ID in {tutorial.Id}: {duplicate.Key}");
            foreach (var step in tutorial.Steps)
            {
                if (step.TargetId is null && step.WebViewTargetId is null && step.Placement != TutorialPlacement.Center)
                    errors.Add($"{tutorial.Id}/{step.Id} has no target and is not centered.");
                if (localizationKeyExists is not null)
                {
                    foreach (string key in new[] { tutorial.TitleKey, tutorial.DescriptionKey, step.TitleKey, step.DescriptionKey, step.TipKey }.OfType<string>())
                        if (!localizationKeyExists(key)) errors.Add($"Missing localization key: {key}");
                }
            }
        }
        return errors;
    }

    private static TutorialStep Step(string id, string? target = null, string? page = null,
        TutorialPlacement placement = TutorialPlacement.Auto, bool optional = false,
        Func<ITutorialContext, bool>? condition = null, string? webTarget = null,
        bool allowInteraction = false, Func<ITutorialContext, CancellationToken, Task>? BeforeShowAsync = null,
        string? image = null) => new()
    {
        Id = id,
        TitleKey = $"Tutorials.Step.{id}.Title",
        DescriptionKey = $"Tutorials.Step.{id}.Description",
        TipKey = null,
        ImagePath = image,
        TargetId = target,
        WebViewTargetId = webTarget,
        PageKey = page,
        Placement = target is null && webTarget is null ? TutorialPlacement.Center : placement,
        IsOptional = optional,
        AllowTargetInteraction = allowInteraction,
        CanShow = condition,
        BeforeShowAsync = BeforeShowAsync
    };

    private static TutorialDefinition Def(string id, int order, string category, bool recommended, params TutorialStep[] steps) =>
        Def(id, order, category, recommended, false, steps);

    private static TutorialDefinition Def(string id, int order, string category, bool recommended, bool newFeature, params TutorialStep[] steps) => new()
    {
        Id = id,
        Version = 1,
        Category = category,
        TitleKey = $"Tutorials.{id}.Title",
        DescriptionKey = $"Tutorials.{id}.Description",
        DisplayOrder = order,
        IsRecommended = recommended,
        IsNewFeature = newFeature,
        Steps = steps
    };

    private static IReadOnlyList<TutorialDefinition> CreateDefinitions() =>
    [
        Def("application-basics", 10, "Getting Started", true,
            Step("basics.navigation", "Navigation.Main", "devices", TutorialPlacement.Right),
            Step("basics.collapse", "Navigation.Collapse", "devices", TutorialPlacement.Right),
            Step("basics.server", "Servers.Current", "devices", TutorialPlacement.Right),
            Step("basics.connection", "Servers.PairingService", "devices", TutorialPlacement.Right),
            Step("basics.notifications", "Notifications.Center", "notifications", TutorialPlacement.Right),
            Step("basics.tutorials", "Tutorials.NavigationItem", "devices", TutorialPlacement.Top)),

        Def("sidebar-folders", 15, "Getting Started", recommended: false, newFeature: true,
            Step("folders.reorder", "Sidebar.SingleTab", "devices", TutorialPlacement.Right),
            Step("folders.create", "Sidebar.SecondTab", "devices", TutorialPlacement.Right),
            Step("folders.unfold", "Sidebar.ToolsFolder", "devices", TutorialPlacement.Right),
            Step("folders.organize", "Sidebar.ToolsFolder", "devices", TutorialPlacement.Right)),

        Def("account-cloud", 20, "Getting Started", false,
            Step("account.profile", "Settings.Cloud", "settings", TutorialPlacement.Right),
            Step("account.cloud", "Settings.Cloud", "settings", TutorialPlacement.Right),
            Step("account.premium", "Settings.Cloud", "settings", TutorialPlacement.Right)),

        Def("pairing-servers", 30, "Getting Started", true,
            Step("pairing.overview", "Servers.PairingService", "devices", TutorialPlacement.Right),
            Step("pairing.start", "Servers.PairingService", "devices", TutorialPlacement.Right),
            Step("pairing.list", "Servers.List", "devices", TutorialPlacement.Right, condition: c => c.HasPairedServer),
            Step("pairing.soft", "Servers.ConnectionActions", "devices", TutorialPlacement.Right, condition: c => c.HasSelectedServer),
            Step("pairing.full", "Servers.ConnectionActions", "devices", TutorialPlacement.Right, condition: c => c.HasSelectedServer),
            Step("pairing.disconnect", "Servers.ConnectionActions", "devices", TutorialPlacement.Right, condition: c => c.IsSoftConnected),
            Step("pairing.reset", placement: TutorialPlacement.Center)),

        Def("map-basics", 40, "Maps", true,
            Step("map.canvas", "Map.Canvas", "map", TutorialPlacement.Center),
            Step("map.toolbar", "Map.Toolbar", "map", TutorialPlacement.Bottom),
            Step("map.fit", "Map.ResetView", "map", TutorialPlacement.Bottom),
            Step("map.layers", "Map.Layers", "map", TutorialPlacement.Bottom),
            Step("map.heatmaps", "Map.Heatmaps", "map", TutorialPlacement.Left, optional: true),
            Step("map.open3d", "Map.Open3D", "map", TutorialPlacement.Left),
            Step("map.drawing", "Map.Drawing", "map", TutorialPlacement.Bottom)),

        Def("heatmaps", 50, "Maps", false,
            Step("heatmaps.open", "Map.ServerHud", "map", TutorialPlacement.Right),
            Step("heatmaps.selector", "Map.Heatmaps", "map", TutorialPlacement.Right, condition: c => c.IsFullConnected),
            Step("heatmaps.meaning", placement: TutorialPlacement.Center)),

        Def("mini-map", 55, "Maps", false,

            Step("minimap.open", "Map.MiniMap", "map", TutorialPlacement.Bottom, condition: c => c.IsFullConnected),
            Step("minimap.settings", placement: TutorialPlacement.Center, condition: c => c.IsFullConnected, 
                 BeforeShowAsync: async (c, ct) => { 
                     Application.Current.Dispatcher.Invoke(() => {
                         Application.Current.MainWindow?.GetType().GetMethod("EnsureMiniMapOpen")?.Invoke(Application.Current.MainWindow, null);
                     }); 
                     await Task.Delay(150, ct); 
                 }),
            Step("minimap.timepop", placement: TutorialPlacement.Center, condition: c => c.IsFullConnected,
                 BeforeShowAsync: async (c, ct) => { 
                     Application.Current.Dispatcher.Invoke(() => {
                         // Find MiniMapWindow from opened windows
                         var mmw = Application.Current.Windows.Cast<Window>().FirstOrDefault(window => window.GetType().Name == "MiniMapWindow");
                         if (mmw != null)
                         {
                             if (mmw.GetType().GetProperty("SettingsOverlay")?.GetValue(mmw) is UIElement overlay)
                                 overlay.Visibility = Visibility.Visible;
                             if (mmw.GetType().GetProperty("SettingsHoverBorder")?.GetValue(mmw) is UIElement hoverBorder)
                                 hoverBorder.Visibility = Visibility.Collapsed;
                         }
                     }); 
                     await Task.Delay(150, ct); 
                 }),
            Step("minimap.controls", placement: TutorialPlacement.Center, condition: c => c.IsFullConnected),
            Step("minimap.follow", "BtnFollowPlayer", "map", TutorialPlacement.Bottom, condition: c => c.IsFullConnected)),

        Def("map-3d", 60, "Maps", false,
            Step("map3d.open", "Map.Open3D", "map", TutorialPlacement.Left, allowInteraction: true),
            Step("map3d.account", "Settings.Cloud", "settings", TutorialPlacement.Right, allowInteraction: true),
            Step("map3d.viewport", page: "map", webTarget: "threeD.viewport", condition: c => c.HasMap),
            Step("map3d.toolbar", page: "map", webTarget: "threeD.toolbar", condition: c => c.HasMap),
            Step("map3d.build", page: "map", webTarget: "threeD.buildMode", condition: c => c.HasMap),
            Step("map3d.performance", page: "map", webTarget: "threeD.performance", condition: c => c.HasMap),
            Step("map3d.close", page: "map", webTarget: "threeD.close", condition: c => c.HasMap)),

        Def("smart-devices", 70, "Devices", true,
            Step("devices.overview", "Devices.List", "devices", TutorialPlacement.Right),
            Step("devices.search", "Devices.Search", "devices", TutorialPlacement.Right),
            Step("devices.filter", "Devices.TypeFilter", "devices", TutorialPlacement.Right),
            Step("devices.refresh", "Devices.Refresh", "devices", TutorialPlacement.Top),
            Step("devices.actions", "Devices.Actions", "devices", TutorialPlacement.Top),
            Step("devices.first", "Devices.Item.FirstAvailable", "devices", TutorialPlacement.Right, optional: true, condition: c => c.HasDevices),
            Step("devices.safety", placement: TutorialPlacement.Center)),

        Def("team-tracking", 80, "Team and Communication", false,
            Step("team.list", "Team.List", "team", TutorialPlacement.Right),
            Step("team.markers", "Team.Markers", "team", TutorialPlacement.Top, condition: c => c.HasTeam),
            Step("team.follow", "Team.List", "team", TutorialPlacement.Right, condition: c => c.HasTeam),
            Step("team.sharing", "Team.List", "team", TutorialPlacement.Right)),

        Def("chat-commands", 90, "Team and Communication", false,
            Step("chat.history", "Chat.History", "map", TutorialPlacement.Left),
            Step("chat.input", "Chat.Input", "map", TutorialPlacement.Top),
            Step("chat.send", "Chat.Send", "map", TutorialPlacement.Top),
            Step("chat.commands", "Settings.ChatCommands", "settings", TutorialPlacement.Right)),

        Def("discord", 100, "Team and Communication", false,
            Step("discord.entry", "Settings.Cloud", "settings", TutorialPlacement.Right),
            Step("discord.settings", "Settings.Cloud", "settings", TutorialPlacement.Right),
            Step("discord.advanced", "Settings.DiscordAdvanced", "settings", TutorialPlacement.Right),
            Step("discord.basic", "Settings.DiscordBasic", "settings", TutorialPlacement.Right)),

        Def("automation", 110, "Automation", false,
            Step("automation.open", "Automation.Open", "devices", TutorialPlacement.Top),
            Step("automation.list", "Automation.Rules", "logic", TutorialPlacement.Right),
            Step("automation.enabled", "Automation.Enabled", "logic", TutorialPlacement.Right),
            Step("automation.workflow", placement: TutorialPlacement.Center),
            Step("automation.safety", placement: TutorialPlacement.Center)),

        Def("notifications", 120, "Monitoring", false,
            Step("notifications.center", "Notifications.Center", "notifications", TutorialPlacement.Right),
            Step("notifications.filters", "Notifications.Filters", "notifications", TutorialPlacement.Right),
            Step("notifications.search", "Notifications.Search", "notifications", TutorialPlacement.Right),
            Step("notifications.actions", "Notifications.Actions", "notifications", TutorialPlacement.Top)),

        Def("cameras", 130, "Monitoring", false,
            Step("cameras.list", "Cameras.List", "cameras", TutorialPlacement.Right),
            Step("cameras.add", "Cameras.Add", "cameras", TutorialPlacement.Right),
            Step("cameras.privacy", placement: TutorialPlacement.Center)),

        Def("raid-calculator", 150, "Tools", false, true,
            Step("raid.search", "Raid.Search", "raid", TutorialPlacement.Right),
            Step("raid.plan", "Raid.Plan", "raid", TutorialPlacement.Left),
            Step("raid.methods", "Raid.Methods", "raid", TutorialPlacement.Left),
            Step("raid.clear", "Raid.Clear", "raid", TutorialPlacement.Bottom)),

        Def("recycler-calculator", 160, "Tools", false,
            Step("recycler.search", "Recycler.Search", "recycler", TutorialPlacement.Right),
            Step("recycler.category", "Recycler.Category", "recycler", TutorialPlacement.Right),
            Step("recycler.inputs", "Recycler.Inputs", "recycler", TutorialPlacement.Right),
            Step("recycler.results", "Recycler.Results", "recycler", TutorialPlacement.Left)),

        Def("settings", 170, "Settings and Support", false,
            Step("settings.open", "Settings.Panel", "settings", TutorialPlacement.Right),
            Step("settings.language", "Settings.Language", "settings", TutorialPlacement.Right),
            Step("settings.map", "Settings.Map", "settings", TutorialPlacement.Right),
            Step("settings.maintenance", "Settings.Maintenance", "settings", TutorialPlacement.Right)),

        Def("updates-diagnostics", 180, "Settings and Support", false,
            Step("updates.check", placement: TutorialPlacement.Center),
            Step("updates.status", placement: TutorialPlacement.Center),
            Step("updates.logs", placement: TutorialPlacement.Center)),

        Def("shops-vending", 190, "Monitoring", false,
            Step("shops.open", "Shops.Open", "shops", TutorialPlacement.Top),
            Step("shops.panel", "Shops.Panel", "shops", TutorialPlacement.Left),
            Step("shops.refresh", placement: TutorialPlacement.Center)),

        Def("events-monitoring", 200, "Monitoring", false,
            Step("events.dock", "Events.Dock", "map", TutorialPlacement.Left),
            Step("events.timer", "Events.Timer", "map", TutorialPlacement.Left),
            Step("events.stale", placement: TutorialPlacement.Center)),

        // Deliberately centred with no targets except the Logic Engine button. Most of what
        // this teaches happens inside Rust, not in the app, so pointing at controls would
        // only be half the story — the wiring diagram is the important part.
        Def("oilrig-crate-alerts", 205, "Monitoring", false, true,
            Step("oilrigcrate.intro", placement: TutorialPlacement.Center),
            Step("oilrigcrate.wiring", placement: TutorialPlacement.Center,
                 image: "pack://application:,,,/Assets/Screenshots/8.0/SmartAlarmOilrig.png"),
            Step("oilrigcrate.rule", "Automation.CreateOilRigRule", "logic", TutorialPlacement.Bottom),
            Step("oilrigcrate.silence", placement: TutorialPlacement.Center)),

        Def("bases-screenshots", 210, "Maps", false,
            Step("bases.map", "Map.Canvas", "map", TutorialPlacement.Right),
            Step("bases.context", "Map.Canvas", "map", TutorialPlacement.Right),
            Step("bases.safety", "Map.Canvas", "map", TutorialPlacement.Right)),

        Def("device-automation", 220, "Automation", false, true,
            Step("deviceautomation.open", "DeviceAutomation.Open", "devices", TutorialPlacement.Top),
            Step("deviceautomation.actions", "DeviceAutomation.Actions", "device-automation", TutorialPlacement.Right),
            Step("deviceautomation.rules", "DeviceAutomation.Rules", "device-automation", TutorialPlacement.Right),
            Step("deviceautomation.safety", placement: TutorialPlacement.Center)),

        Def("offline-alerts-smarthome", 230, "Automation", false, true,
            Step("offlinealerts.overview", "Settings.OfflineAlerts", "settings", TutorialPlacement.Right),
            Step("offlinealerts.sync", "Settings.OfflineAlerts", "settings", TutorialPlacement.Right),
            Step("offlinealerts.telegram", "Settings.OfflineAlerts", "settings", TutorialPlacement.Right),
            Step("offlinealerts.alexa", "Settings.Alexa", "settings", TutorialPlacement.Right),

            // Two short steps rather than one long one: the first says what to do in Rust, the
            // second what the app does with it. Both point at the device list, because that is
            // where the result becomes visible.
            Step("offlinealerts.alarmnames", "Devices.List", "devices", TutorialPlacement.Right),
            Step("offlinealerts.alarmlearn", "Devices.Item.FirstAvailable", "devices", TutorialPlacement.Right))
    ];
}
