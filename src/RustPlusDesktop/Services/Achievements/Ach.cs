namespace RustPlusDesk.Services.Achievements;

/// <summary>
/// Short name for the call sites.
///
/// Triggers sit wherever the thing actually happens - a tab switch, a poll, a
/// click handler - scattered across the app. Keeping the call to one short line
/// means adding one never restructures the code it lands in, and re-exporting the
/// ids here keeps the catalog the single place they are defined.
/// </summary>
internal static class Ach
{
    public const string SmartDevicePaired = AchievementCatalog.SmartDevicePaired;
    public const string TeamMate = AchievementCatalog.TeamMate;
    public const string CloudSync = AchievementCatalog.CloudSync;
    public const string Map3D = AchievementCatalog.Map3D;
    public const string Widget = AchievementCatalog.Widget;
    public const string Tutorials = AchievementCatalog.Tutorials;
    public const string Automation = AchievementCatalog.Automation;
    public const string LogicRule = AchievementCatalog.LogicRule;
    public const string GeneticsLab = AchievementCatalog.GeneticsLab;
    public const string RaidCalculator = AchievementCatalog.RaidCalculator;
    public const string MiniMap = AchievementCatalog.MiniMap;
    public const string DeathStats = AchievementCatalog.DeathStats;
    public const string Heatmap = AchievementCatalog.Heatmap;
    public const string Keycards = AchievementCatalog.Keycards;
    public const string CameraImage = AchievementCatalog.CameraImage;
    public const string Clan = AchievementCatalog.Clan;
    public const string GlobalChat = AchievementCatalog.GlobalChat;
    public const string Shops = AchievementCatalog.Shops;
    public const string Timer = AchievementCatalog.Timer;
    public const string AiCompanion = AchievementCatalog.AiCompanion;
    public const string Crosshair = AchievementCatalog.Crosshair;
    public const string FarmingRoute = AchievementCatalog.FarmingRoute;
    public const string OilrigCrate = AchievementCatalog.OilrigCrate;
    public const string PatchNotes = AchievementCatalog.PatchNotes;
    public const string CargoSound = AchievementCatalog.CargoSound;
    public const string Lfg = AchievementCatalog.Lfg;
    public const string FollowMe = AchievementCatalog.FollowMe;
    public const string DeepSea = AchievementCatalog.DeepSea;
    public const string Raided = AchievementCatalog.Raided;
    public const string DotMarker = AchievementCatalog.DotMarker;
    public const string ConsoleCommand = AchievementCatalog.ConsoleCommand;

    /// <summary>Cheap and idempotent: safe from a poll loop or a redraw.</summary>
    public static void Unlock(string id) => AchievementService.Unlock(id);
}
