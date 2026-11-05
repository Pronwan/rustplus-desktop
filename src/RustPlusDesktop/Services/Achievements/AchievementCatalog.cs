using System.Collections.Generic;

namespace RustPlusDesk.Services.Achievements;

/// <summary>
/// One achievement. The name is the only hint a player gets - there are
/// deliberately no descriptions, so finding them is part of it.
/// </summary>
public sealed record AchievementDef(
    string Id,
    string Name,
    int SheetIndex,
    bool IsSecret = false);

/// <summary>
/// Every achievement the app knows, in sprite sheet order.
///
/// SheetIndex is the cell in Assets/achievements/achievements_sheet.png, counted
/// left to right and top to bottom; the order here matches the artwork, so index
/// and position stay in step.
/// </summary>
public static class AchievementCatalog
{
    /// <summary>Sprite sheet geometry. The sheet is a scaled copy of the 256px original.</summary>
    public const int CellSize = 128;
    public const int CellGap = 8;
    public const int Columns = 8;

    public const string SheetUri = "pack://application:,,,/Assets/achievements/achievements_sheet.png";

    // Ids are stable: they are what ends up in the save file, so renaming a
    // display name never costs anyone their progress.
    public const string SmartDevicePaired = "im_smarter_now";
    public const string TeamMate = "stronger_together";
    public const string CloudSync = "sharing_is_caring";
    public const string Map3D = "multi_dimensional";
    public const string Widget = "widgets_baby";
    public const string Tutorials = "im_educated";
    public const string Automation = "works_on_its_own";
    public const string LogicRule = "logic_conclusions";
    public const string GeneticsLab = "dharwin_would_be_proud";
    public const string RaidCalculator = "no_gp_wasted";
    public const string MiniMap = "look_its_mini_me";
    public const string DeathStats = "didnt_die_this_wipe";
    public const string Heatmap = "thats_hot";
    public const string Keycards = "what_card_again";
    public const string CameraImage = "i_see_you";
    public const string Clan = "even_stronger";
    public const string GlobalChat = "hello_world";
    public const string Shops = "we_wish_that_was_still_possible";
    public const string Timer = "tick_tick_tick";
    public const string AiCompanion = "is_it_concious";
    public const string Crosshair = "always_in_sight";
    public const string FarmingRoute = "i_know_where_to_farm";
    public const string OilrigCrate = "15m_to_counter_that";
    public const string PatchNotes = "whats_new";
    public const string CargoSound = "tuut_tuut";
    public const string Lfg = "join_me";
    public const string FollowMe = "stay_with_me";
    public const string DeepSea = "going_deep";
    public const string Raided = "getting_raided";
    public const string DotMarker = "im_a_dot";
    public const string ConsoleCommand = "secret_achievement";

    public static readonly IReadOnlyList<AchievementDef> All = new[]
    {
        new AchievementDef(SmartDevicePaired, "I'm smarter now",              0),
        new AchievementDef(TeamMate,          "Stronger Together",           1),
        new AchievementDef(CloudSync,         "Sharing is Caring",           2),
        new AchievementDef(Map3D,             "Multi-Dimensional",           3),
        new AchievementDef(Widget,            "Widgets, Baby",               4),
        new AchievementDef(Tutorials,         "I'm educated",                5),
        new AchievementDef(Automation,        "Works on its own",            6),
        new AchievementDef(LogicRule,         "Logic Conclusions",           7),
        new AchievementDef(GeneticsLab,       "Dharwin would be proud",      8),
        new AchievementDef(RaidCalculator,    "No GP wasted",                9),
        new AchievementDef(MiniMap,           "Look, it's Mini-Me",         10),
        new AchievementDef(DeathStats,        "Didn't die this wipe",       11),
        new AchievementDef(Heatmap,           "That's hot!",                12),
        new AchievementDef(Keycards,          "What card again?",           13),
        new AchievementDef(CameraImage,       "I see you",                  14),
        new AchievementDef(Clan,              "Even Stronger",              15),
        new AchievementDef(GlobalChat,        "Hello World!",               16),
        new AchievementDef(Shops,             "We wish that was still possible", 17),
        new AchievementDef(Timer,             "Tick...Tick...Tick...",      18),
        new AchievementDef(AiCompanion,       "Is it concious?",            19),
        new AchievementDef(Crosshair,         "Always in sight",            20),
        new AchievementDef(FarmingRoute,      "I know where to farm",       21),
        new AchievementDef(OilrigCrate,       "15m to counter that",        22),
        new AchievementDef(PatchNotes,        "What's new?",                23),
        new AchievementDef(CargoSound,        "Tuut Tuut",                  24),
        new AchievementDef(Lfg,               "Join me!",                   25),
        new AchievementDef(FollowMe,          "Stay with me",               26),
        new AchievementDef(DeepSea,           "Going Deep!",                27),
        new AchievementDef(Raided,            "Getting Raided",             28),
        new AchievementDef(DotMarker,         "I'm a dot",                  29),

        // Shows as "???" with no icon until earned, then reveals its real name.
        new AchievementDef(ConsoleCommand,    "Console Command",            30, IsSecret: true),
    };

    public static AchievementDef? Find(string id)
    {
        foreach (var a in All)
        {
            if (a.Id == id) return a;
        }
        return null;
    }
}
