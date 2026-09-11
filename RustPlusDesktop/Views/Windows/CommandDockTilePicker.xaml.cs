using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk
{
    /// <summary>
    /// The catalogue behind the dock's "+": every tile that can be added right now, built from
    /// the live server rather than a fixed list, so a device that was never paired and a rule
    /// that was never written cannot be picked in the first place.
    /// </summary>
    public partial class CommandDockTilePicker : Window
    {
        public ICommandDockHost? Host { get; set; }
        public Action<CommandDockTile>? OnPicked { get; set; }
        public Action? OnClosed { get; set; }

        public CommandDockTilePicker()
        {
            InitializeComponent();
            Closed += (_, __) => OnClosed?.Invoke();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        public void Refresh()
        {
            Catalogue.Children.Clear();

            // Offered only while no map is on the dock — either removed, or with every layer
            // switched off, which looks the same from the outside.
            if ((Owner as MiniMapWindow)?.CanAddMap == true)
            {
                Section(Loc.Text("CommandDockSectionMap", "Map"));
                Entry(Loc.Text("MiniMap", "Mini-map"),
                      Loc.Text("CommandDockAddMapHint", "Brings the map back with all layers on"),
                      () => new CommandDockTile { Kind = CommandDockTileKinds.Map });
            }

            Section(Loc.Text("CommandDockSectionClock", "Clock"));
            Entry(Loc.Text("CommandDockClockDigital", "Digital clock"),
                  Loc.Text("CommandDockClockDigitalHint", "Server time with the day and night phase"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Clock, ClockStyle = 0 });
            Entry(Loc.Text("CommandDockClockAnalog", "Analogue clock"),
                  Loc.Text("CommandDockClockAnalogHint", "A dial that follows the server clock"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Clock, ClockStyle = 1 });
            Entry(Loc.Text("CommandDockClockRust", "Rust clock"),
                  Loc.Text("CommandDockClockRustHint", "Server time in the game's own lettering"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Clock, ClockStyle = 2 });

            Section(Loc.Text("CommandDockSectionSession", "Session"));
            Entry(Loc.Text("CommandDockSessionTitle", "Session stats"),
                  RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium
                      ? Loc.Text("CommandDockSessionHint", "Online time, distance, idle time and deaths")
                      : Loc.Text("CommandDockSupporterOnlyShort", "Supporter feature"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Session, ColSpan = 3, RowSpan = 1 });

            Entry("Discord",
                  RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium
                      ? Loc.Text("CommandDockDiscordHint", "Send a line or the current map view")
                      : Loc.Text("CommandDockSupporterOnlyShort", "Supporter feature"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Discord, ColSpan = 2, RowSpan = 1 });

            Section(Loc.Text("CommandDockSectionAi", "AI companion"));
            Entry(Loc.Text("CommandDockAiTitle", "Ask AI"),
                  RustPlusDesk.Services.AiCompanion.AiCompanionStore.HasKey
                      ? Loc.Text("CommandDockAiPickerHint", "Record a question, attach the screen, get an answer")
                      : Loc.Text("CommandDockAiPickerNoKey", "Needs your own AI key under Connected Services"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.AiCompanion, ColSpan = 2, RowSpan = 1 });

            Section(Loc.Text("CommandDockSectionChat", "Chat"));
            Entry(Loc.Text("TeamChat", "Team chat"),
                  Loc.Text("CommandDockChatHint", "Two cells wide, resizable in edit mode"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.TeamChat, ColSpan = 2, RowSpan = 2 });
            Entry(Loc.Text("ClanChat", "Clan chat"),
                  Loc.Text("CommandDockChatHint", "Two cells wide, resizable in edit mode"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.ClanChat, ColSpan = 2, RowSpan = 2 });

            Section(Loc.Text("CommandDockSectionServer", "Server"));
            Entry(Loc.Text("CommandDockServerInfoTitle", "Server info"),
                  Loc.Text("CommandDockServerInfoHint", "Players on now, and how that has moved this session"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.ServerInfo, ColSpan = 2, RowSpan = 1 });

            // Heli, Chinook and the travelling vendor are gone on purpose: the event dock hides
            // them whenever the server does not report them, and a tile that is empty on most
            // servers is worse than no tile at all.
            Section(Loc.Text("CommandDockSectionEvents", "Events"));
            foreach (var (key, label) in new[]
                     {
                         ("cargo", Loc.Text("CargoShip", "Cargo ship")),
                         ("deepsea", Loc.Text("DeepSea", "Deep Sea")),
                         ("oilrig", Loc.Text("OilRigCrateStatus", "Oil Rig crate")),
                     })
            {
                var eventKey = key;
                Entry(label, "", () => new CommandDockTile { Kind = CommandDockTileKinds.Event, EventKey = eventKey });
            }

            var devices = Host?.DockDevices ?? Array.Empty<SmartDevice>();
            if (devices.Count > 0)
            {
                Section(Loc.Text("CommandDockSectionDevices", "Devices"));
                // Stamped with the server it was added on: an entity id means nothing on another
                // one, so the tile only appears where it can actually do something.
                var serverKey = Host?.DockServerKey;

                foreach (var device in devices.Where(d => !d.IsGroup).OrderBy(d => d.DisplayName))
                {
                    var entityId = device.EntityId;
                    Entry(device.DisplayName, device.Kind ?? "",
                          () => new CommandDockTile
                          {
                              Kind = CommandDockTileKinds.Device,
                              EntityId = entityId,
                              ServerKey = serverKey,
                          });
                }
            }

            // Only rules that asked to be launched this way. A rule triggered by an alarm has
            // its own timing, and putting a button on it would fire it out of turn.
            var rules = (Host?.DockRules ?? Array.Empty<LogicRule>())
                .Where(r => r.TriggerType == "CommandDock")
                .ToList();

            Section(Loc.Text("CommandDockSectionRules", "Logic Engine"));
            if (rules.Count == 0)
            {
                Catalogue.Children.Add(new TextBlock
                {
                    Text = Loc.Text("CommandDockNoRules",
                        "No rule uses the Command Dock trigger yet. Set a rule's trigger to Command Dock to launch it from here."),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Application.Current?.TryFindResource("TextSubtle") as Brush ?? Brushes.Gray,
                });
            }
            else
            {
                foreach (var rule in rules)
                {
                    var ruleId = rule.Id;
                    var hint = rule.IsEnabled
                        ? Loc.Text("CommandDockRuleReady", "Runs on click")
                        : Loc.Text("CommandDockRuleInactive", "Activate Logic Engine Mechanics or Rule");
                    Entry(rule.Name, hint,
                          () => new CommandDockTile { Kind = CommandDockTileKinds.Rule, RuleId = ruleId });
                }
            }
        }

        private void Section(string title)
        {
            Catalogue.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, Catalogue.Children.Count == 0 ? 0 : 12, 0, 4),
                Foreground = Application.Current?.TryFindResource("Accent") as Brush ?? Brushes.SkyBlue,
            });
        }

        private void Entry(string title, string subtitle, Func<CommandDockTile> make)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            if (!string.IsNullOrEmpty(subtitle))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 10,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Application.Current?.TryFindResource("TextSubtle") as Brush ?? Brushes.Gray,
                });
            }

            var button = new Button
            {
                Content = stack,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
            };

            // The picker stays open on purpose: adding four tiles in a row is the normal case,
            // and reopening it between each would make building a quickbar tedious.
            button.Click += (_, __) => OnPicked?.Invoke(make());

            Catalogue.Children.Add(button);
        }
    }
}
