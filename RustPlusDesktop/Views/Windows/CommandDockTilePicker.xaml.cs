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
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

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
        public new Action? OnClosed { get; set; }

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
                Section(Loc.Text("CommandDockSectionMap", "Map"), SymbolRegular.Map24);
                Entry(Loc.Text("MiniMap", "Mini-map"),
                      Loc.Text("CommandDockAddMapHint", "Brings the map back with all layers on"),
                      () => new CommandDockTile { Kind = CommandDockTileKinds.Map },
                      SymbolRegular.Map24);
            }

            Section(Loc.Text("CommandDockSectionClock", "Clock"), SymbolRegular.Clock24);
            Entry(Loc.Text("CommandDockClockDigital", "Digital clock"),
                  Loc.Text("CommandDockClockDigitalHint", "Server time with the day and night phase"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Clock, ClockStyle = 0 },
                  SymbolRegular.Clock24);
            Entry(Loc.Text("CommandDockClockAnalog", "Analogue clock"),
                  Loc.Text("CommandDockClockAnalogHint", "A dial that follows the server clock"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Clock, ClockStyle = 1 },
                  SymbolRegular.Timer24);
            Entry(Loc.Text("CommandDockClockRust", "Rust clock"),
                  Loc.Text("CommandDockClockRustHint", "Server time in the game's own lettering"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Clock, ClockStyle = 2 },
                  SymbolRegular.Clock24);

            Section(Loc.Text("CommandDockSectionSession", "Session"), SymbolRegular.DataUsage24);
            Entry(Loc.Text("CommandDockSessionTitle", "Session stats"),
                  RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium
                      ? Loc.Text("CommandDockSessionHint", "Online time, distance, idle time and deaths")
                      : Loc.Text("CommandDockSupporterOnlyShort", "Supporter feature"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Session, ColSpan = 3, RowSpan = 1 },
                  SymbolRegular.DataUsage24);

            Entry("Discord",
                  RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium
                      ? Loc.Text("CommandDockDiscordHint", "Send a line or the current map view")
                      : Loc.Text("CommandDockSupporterOnlyShort", "Supporter feature"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Discord, ColSpan = 2, RowSpan = 1 },
                  SymbolRegular.ChatMultiple24);

            Section(Loc.Text("CommandDockSectionAi", "AI companion"), SymbolRegular.Sparkle24);
            Entry(Loc.Text("CommandDockAiTitle", "Ask AI"),
                  RustPlusDesk.Services.AiCompanion.AiCompanionStore.HasKey
                      ? Loc.Text("CommandDockAiPickerHint", "Record a question, attach the screen, get an answer")
                      : Loc.Text("CommandDockAiPickerNoKey", "Needs your own AI key under Connected Services"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.AiCompanion, ColSpan = 2, RowSpan = 1 },
                  SymbolRegular.Sparkle24);

            Entry(Loc.Text("CommandDockTranslateTitle", "Translate"),
                  Loc.Text("CommandDockTranslatePickerHint",
                      "Paste a line, read it back in your language — or dictate it"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.Translate, ColSpan = 3, RowSpan = 1 },
                  SymbolRegular.Translate24);

            Section(Loc.Text("CommandDockSectionChat", "Chat"), SymbolRegular.Chat24);
            Entry(Loc.Text("TeamChat", "Team chat"),
                  Loc.Text("CommandDockChatHint", "Two cells wide, resizable in edit mode"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.TeamChat, ColSpan = 2, RowSpan = 2 },
                  SymbolRegular.PeopleCommunity24);
            Entry(Loc.Text("ClanChat", "Clan chat"),
                  Loc.Text("CommandDockChatHint", "Two cells wide, resizable in edit mode"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.ClanChat, ColSpan = 2, RowSpan = 2 },
                  SymbolRegular.Shield24);

            Section(Loc.Text("CommandDockSectionServer", "Server"), SymbolRegular.Server24);
            Entry(Loc.Text("CommandDockServerInfoTitle", "Server info"),
                  Loc.Text("CommandDockServerInfoHint", "Players on now, and how that has moved this session"),
                  () => new CommandDockTile { Kind = CommandDockTileKinds.ServerInfo, ColSpan = 2, RowSpan = 1 },
                  SymbolRegular.Server24);

            // Heli, Chinook and the travelling vendor are gone on purpose: the event dock hides
            // them whenever the server does not report them, and a tile that is empty on most
            // servers is worse than no tile at all.
            Section(Loc.Text("CommandDockSectionEvents", "Events"), SymbolRegular.AlertUrgent24);
            foreach (var (key, label, sym) in new[]
                     {
                         ("cargo", Loc.Text("CargoShip", "Cargo ship"), SymbolRegular.VehicleShip24),
                         ("deepsea", Loc.Text("DeepSea", "Deep Sea"), SymbolRegular.Water24),
                         ("oilrig", Loc.Text("OilRigCrateStatus", "Oil Rig crate"), SymbolRegular.Box24),
                     })
            {
                var eventKey = key;
                Entry(label, "", () => new CommandDockTile { Kind = CommandDockTileKinds.Event, EventKey = eventKey }, sym);
            }

            var devices = Host?.DockDevices ?? Array.Empty<SmartDevice>();
            if (devices.Count > 0)
            {
                Section(Loc.Text("CommandDockSectionDevices", "Devices"), SymbolRegular.PlugConnected24);
                // Stamped with the server it was added on: an entity id means nothing on another
                // one, so the tile only appears where it can actually do something.
                var serverKey = Host?.DockServerKey;

                foreach (var device in devices.Where(d => !d.IsGroup).OrderBy(d => d.DisplayName))
                {
                    var entityId = device.EntityId;
                    var sym = device.Kind switch
                    {
                        "SmartSwitch" or "Smart Switch" => SymbolRegular.Power24,
                        "StorageMonitor" => SymbolRegular.Database24,
                        "SmartAlarm" => SymbolRegular.Alert24,
                        _ => SymbolRegular.PlugConnected24
                    };

                    Entry(device.DisplayName, device.Kind ?? "",
                          () => new CommandDockTile
                          {
                              Kind = CommandDockTileKinds.Device,
                              EntityId = entityId,
                              ServerKey = serverKey,
                          }, sym);
                }
            }

            // Only rules that asked to be launched this way. A rule triggered by an alarm has
            // its own timing, and putting a button on it would fire it out of turn.
            var rules = (Host?.DockRules ?? Array.Empty<LogicRule>())
                .Where(r => r.TriggerType == "CommandDock")
                .ToList();

            Section(Loc.Text("CommandDockSectionRules", "Logic Engine"), SymbolRegular.BrainCircuit24);
            if (rules.Count == 0)
            {
                Catalogue.Children.Add(new TextBlock
                {
                    Text = Loc.Text("CommandDockNoRules",
                        "No rule uses the Command Dock trigger yet. Set a rule's trigger to Command Dock to launch it from here."),
                    FontSize = 10.5,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 2, 4, 6),
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
                          () => new CommandDockTile { Kind = CommandDockTileKinds.Rule, RuleId = ruleId },
                          SymbolRegular.Branch24);
                }
            }
        }

        private void Section(string title, SymbolRegular? icon = null)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(2, Catalogue.Children.Count == 0 ? 2 : 12, 0, 5)
            };

            if (icon.HasValue)
            {
                panel.Children.Add(new SymbolIcon
                {
                    Symbol = icon.Value,
                    FontSize = 12,
                    Foreground = Application.Current?.TryFindResource("Accent") as Brush ?? Brushes.SkyBlue,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Application.Current?.TryFindResource("Accent") as Brush ?? Brushes.SkyBlue,
                VerticalAlignment = VerticalAlignment.Center
            });

            Catalogue.Children.Add(panel);
        }

        private void Entry(string title, string subtitle, Func<CommandDockTile> make, SymbolRegular icon = SymbolRegular.AppGeneric24)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1E, 0x27)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x2E, 0x3D)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon in badge
            var iconBadge = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x28, 0x36)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x37, 0x4A)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBadge.Child = new SymbolIcon
            {
                Symbol = icon,
                FontSize = 14,
                Foreground = Application.Current?.TryFindResource("Accent") as Brush ?? Brushes.SkyBlue,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconBadge, 0);
            grid.Children.Add(iconBadge);

            // Text Info
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Application.Current?.TryFindResource("TextPrimary") as Brush ?? Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (!string.IsNullOrEmpty(subtitle))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    FontSize = 10,
                    Foreground = Application.Current?.TryFindResource("TextSubtle") as Brush ?? Brushes.Gray,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 1, 0, 0)
                });
            }
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            // Right Chevron
            var rightIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.ChevronRight24,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0x94, 0xA0, 0xB3)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(rightIcon, 2);
            grid.Children.Add(rightIcon);

            card.Child = grid;

            // Fluent Hover State
            card.MouseEnter += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x37));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x38, 0x47, 0x5C));
            };
            card.MouseLeave += (s, e) =>
            {
                card.Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1E, 0x27));
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x2E, 0x3D));
            };
            card.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;
                OnPicked?.Invoke(make());
            };

            Catalogue.Children.Add(card);
        }
    }
}
