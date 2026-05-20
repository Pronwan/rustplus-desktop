using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private class TwoStepFlip
    {
        public RustPlusClientReal.ShopMarker ShopFirst;
        public RustPlusClientReal.ShopMarker ShopSecond;
        public RustPlusClientReal.ShopOrder OfferFirst;
        public RustPlusClientReal.ShopOrder OfferSecond;

        public string StartCurrencyName = "";  // Währung, mit der wir anfangen und am Ende wieder rauskommen
        public string MidItemName = "";        // Zwischen-Item

        public int RunsFirst;                  // wie oft wir Schritt1 ausgeführt haben
        public int RunsSecond;                 // wie oft Schritt2

        public int StartSpent;                 // wieviel StartCurrency wir investiert haben
        public int StartBack;                  // wieviel StartCurrency wir zurückbekommen haben
        public int Profit;                     // StartBack - StartSpent

        public int MidProduced;                // wieviel MidItem nach Schritt1 insgesamt
        public int MidConsumed;                // wieviel MidItem von Schritt2 verbraucht
        public int MidLeftover;                // Rest MidItem danach
    }

    // Simuliert: erst (shop1,o1) mehrfach laufen lassen, dann (shop2,o2) benutzen.
    // Gibt BESTE profitable Kombination zurück oder null.
    private TwoStepFlip? SimulateSequence(
    RustPlusClientReal.ShopMarker shop1, RustPlusClientReal.ShopOrder o1,
    RustPlusClientReal.ShopMarker shop2, RustPlusClientReal.ShopOrder o2)
    {
        // Daten aus o1
        int pay1Id = o1.CurrencyItemId;
        string pay1Name = ResolveItemName(o1.CurrencyItemId, o1.CurrencyShortName ?? "");
        int pay1Amt = (int)o1.CurrencyAmount;

        int get1Id = o1.ItemId;
        string get1Name = ResolveItemName(o1.ItemId, o1.ItemShortName ?? "");
        int get1Amt = o1.Quantity;

        int stock1 = o1.Stock;

        // Daten aus o2
        int pay2Id = o2.CurrencyItemId;
        string pay2Name = ResolveItemName(o2.CurrencyItemId, o2.CurrencyShortName ?? "");
        int pay2Amt = (int)o2.CurrencyAmount;

        int get2Id = o2.ItemId;
        string get2Name = ResolveItemName(o2.ItemId, o2.ItemShortName ?? "");
        int get2Amt = o2.Quantity;

        int stock2 = o2.Stock;

        // Guards
        if (pay1Amt <= 0 || get1Amt <= 0 || stock1 <= 0) return null;
        if (pay2Amt <= 0 || get2Amt <= 0 || stock2 <= 0) return null;

        // Loop-Bedingung jetzt über IDs, nicht Strings:
        // Schritt1 produziert get1Id -> muss Schritt2 bezahlen pay2Id
        if (get1Id != pay2Id) return null;
        // Schritt2 produziert get2Id -> muss wieder der Ursprungs-Start pay1Id sein
        if (get2Id != pay1Id) return null;

        TwoStepFlip? best = null;

        for (int runs1 = 1; runs1 <= stock1; runs1++)
        {
            int spentStart = runs1 * pay1Amt;   // wieviel Startwährung investiert
            int midProduced = runs1 * get1Amt;   // wieviel Zwischen-Item bekommen

            int maxByMid = midProduced / pay2Amt;
            int runs2 = Math.Min(maxByMid, stock2);
            if (runs2 <= 0) continue;

            int midConsumed = runs2 * pay2Amt;
            int midLeft = midProduced - midConsumed;
            int startBack = runs2 * get2Amt;

            int profit = startBack - spentStart;
            if (profit <= 0) continue;

            if (best == null || profit > best.Profit)
            {
                best = new TwoStepFlip
                {
                    ShopFirst = shop1,
                    ShopSecond = shop2,
                    OfferFirst = o1,
                    OfferSecond = o2,

                    StartCurrencyName = pay1Name,
                    MidItemName = get1Name,

                    RunsFirst = runs1,
                    RunsSecond = runs2,

                    StartSpent = spentStart,
                    StartBack = startBack,
                    Profit = profit,

                    MidProduced = midProduced,
                    MidConsumed = midConsumed,
                    MidLeftover = midLeft
                };
            }
        }

        return best;
    }

    private List<TwoStepFlip> FindTwoStepFlips(List<RustPlusClientReal.ShopMarker> shops)
    {
        var flips = new List<TwoStepFlip>();

        // zum Deduplizieren
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < shops.Count; i++)
        {
            var s1 = shops[i];
            if (s1.Orders == null) continue;

            for (int j = 0; j < shops.Count; j++)
            {
                var s2 = shops[j];
                if (s2.Orders == null) continue;

                foreach (var o1 in s1.Orders)
                {
                    if (o1.Stock <= 0) continue;
                    foreach (var o2 in s2.Orders)
                    {
                        if (o2.Stock <= 0) continue;

                        // Versuch A->B->A
                        var fwd = SimulateSequence(s1, o1, s2, o2);
                        if (fwd != null)
                        {
                            string sig = MakeFlipSignature(fwd);
                            if (seen.Add(sig))
                                flips.Add(fwd);
                        }

                        // Versuch B->A->B (umgekehrt)
                        var rev = SimulateSequence(s2, o2, s1, o1);
                        if (rev != null)
                        {
                            string sig = MakeFlipSignature(rev);
                            if (seen.Add(sig))
                                flips.Add(rev);
                        }
                    }
                }
            }
        }

        // sortiere: höchster Profit zuerst
        flips.Sort((a, b) => b.Profit.CompareTo(a.Profit));
        return flips;
    }

    // Eindeutige Signatur, damit wir Duplikate filtern
    private string MakeFlipSignature(TwoStepFlip f)
    {
        // Wir sortieren die Shop-IDs, damit A→B & B→A gleich behandelt werden
        uint a = f.ShopFirst.Id;
        uint b = f.ShopSecond.Id;
        if (a > b) { var tmp = a; a = b; b = tmp; }

        // Wir nehmen die ItemIds, nicht die hübschen Namen
        int startId = f.OfferFirst.CurrencyItemId; // Startwährung
        int midId = f.OfferFirst.ItemId;         // Zwischen-Item

        return $"{startId}|{midId}|{a}|{b}";
    }

    private FrameworkElement BuildFlipCard(TwoStepFlip f)
    {
        // äußerer Rahmen des gesamten Flips
        var outerBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(28, 30, 33)), // dunkler statt halbtransparent
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var outerStack = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        outerBorder.Child = outerStack;

        // === HEADER mit großem Icon + Profit-Text ===
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)), // dunkler Streifen
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerBorder.Child = headerRow;

        // Großes Icon für die Start-Ressource (das, wo du am Ende mehr davon hast)
        var bigIcon = new Image
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        // OfferFirst.Currency = das Start-Item
        BindIcon(
            bigIcon,
            f.OfferFirst.CurrencyShortName,
            f.OfferFirst.CurrencyItemId
        );
        headerRow.Children.Add(bigIcon);

        // Rechts daneben Textblock(e)
        var headerTextStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };

        // fette Profit-Zeile
        headerTextStack.Children.Add(new TextBlock
        {
            Text = $"Profit: +{f.Profit} {f.StartCurrencyName}",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 220, 100)), // gold-ish
            FontWeight = FontWeights.SemiBold,
            FontSize = 14
        });

        // Kurze Zusammenfassung Start -> End
        headerTextStack.Children.Add(new TextBlock
        {
            Text =
        $"Start with {f.StartSpent} {f.StartCurrencyName} → end with {f.StartBack} {f.StartCurrencyName} " +
        $"(Step1 x{f.RunsFirst}, Step2 x{f.RunsSecond})",
            Foreground = Brushes.White,
            Margin = new Thickness(0, 2, 0, 0),
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWithOverflow
        });

        headerRow.Children.Add(headerTextStack);

        outerStack.Children.Add(headerBorder);

        // === Info über die Zwischen-Ressource ===
        // z.B. "Intermediate Crude Oil: made 100, used 15, leftover 85"
        outerStack.Children.Add(new TextBlock
        {
            Text =
                $"Intermediate {f.MidItemName}: made {f.MidProduced}, used {f.MidConsumed}, leftover {f.MidLeftover}",
            Foreground = new SolidColorBrush(Color.FromArgb(200, 220, 220, 220)),
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 12,
            TextWrapping = TextWrapping.WrapWithOverflow
        });

        // === Zwei Spalten: Step 1 links, Step 2 rechts ===
        var stepsGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 0)
        };
        stepsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stepsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Linke Spalte = Step1
        var step1Panel = BuildFlipStepPanel(
            stepLabel: $"Step 1 x{f.RunsFirst}",
            shop: f.ShopFirst,
            order: f.OfferFirst
        );
        Grid.SetColumn(step1Panel, 0);
        stepsGrid.Children.Add(step1Panel);

        // Rechte Spalte = Step2
        var step2Panel = BuildFlipStepPanel(
            stepLabel: $"Step 2 x{f.RunsSecond}",
            shop: f.ShopSecond,
            order: f.OfferSecond
        );
        Grid.SetColumn(step2Panel, 1);
        stepsGrid.Children.Add(step2Panel);

        outerStack.Children.Add(stepsGrid);

        return outerBorder;
    }

    private FrameworkElement BuildFlipStepPanel(string stepLabel,
    RustPlusClientReal.ShopMarker shop,
    RustPlusClientReal.ShopOrder order)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 8, 0)
        };

        // Step headline
        panel.Children.Add(new TextBlock
        {
            Text = stepLabel,
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold
        });

        // Shop row with [Go] button
        var shopRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 2, 0, 4),
            VerticalAlignment = VerticalAlignment.Center
        };

        shopRow.Children.Add(new TextBlock
        {
            Text = $"{(shop.Label ?? "Shop")} [{GetGridLabel(shop)}]",
            Foreground = SearchText,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 160
        });

        var btnGo = new Button
        {
            Content = "Go",
            Margin = new Thickness(0, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        btnGo.Click += (_, __) => CenterMapOnWorld(shop.X, shop.Y);

        shopRow.Children.Add(btnGo);

        panel.Children.Add(shopRow);

        // Offer card itself (re-use BuildOfferRowUI so wir kriegen Icons/Stock/→ usw.)
        var offerCard = BuildOfferRowUI(order);
        // BuildOfferRowUI gibt dir schon einen Border mit Grid drin.
        // Wir wollen es nur etwas einrücken:
        if (offerCard is FrameworkElement fe)
            fe.Margin = new Thickness(0, 0, 8, 8);

        panel.Children.Add(offerCard);

        return panel;
    }


    private void RunShopAnalysis()
    {
        if (_analysisList == null) return;

        // TODO: tatsächliche Arbitrage-Logik bauen.
        // Für jetzt nur ein Placeholder, damit's kompiliert.
        _analysisList.Items.Clear();
        _analysisList.Items.Add(new TextBlock
        {
            Text = "Analysis coming soon...",
            Foreground = SearchText
        });
        _analysisList.Visibility = Visibility.Visible;
    }

    private TextBox? _wantTb;
    private TextBox? _payTb;
    private Button? _runPathBtn;
    private ListBox? _pathResultList;
    private ListBox? _wantPreviewList;
    private ListBox? _payPreviewList;
    private bool _pathFinderInitialized;
    private void OpenPathFinderWindow()
    {
        if (BuyXForYPanel.Visibility == Visibility.Visible)
        {
            BuyXForYPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ProfitTradesPanel.Visibility = Visibility.Collapsed;

        if (!_pathFinderInitialized)
        {
            _pathFinderInitialized = true;
            _wantTb = BuyXForY_WantTb;
            _payTb = BuyXForY_PayTb;
            _runPathBtn = BuyXForY_AnalyzeBtn;
            _pathResultList = BuyXForY_ResultList;
            _wantPreviewList = BuyXForY_WantPreview;
            _payPreviewList = BuyXForY_PayPreview;

            _wantTb.TextChanged += (_, __) => RefreshPathfinderPreviews();
            _payTb.TextChanged += (_, __) => RefreshPathfinderPreviews();
            _runPathBtn.Click += (_, __) => RunPathAnalysis();
            BtnCloseBuyXForY.Click += (_, __) => BuyXForYPanel.Visibility = Visibility.Collapsed;
        }

        BuyXForYPanel.Visibility = Visibility.Visible;
        UpdateShopSearchToolHighlights();
    }
    // Style for Scroll bars
   
    private static void ApplyThinScrollbar(FrameworkElement target)
    {
        const string xaml = @"
<ResourceDictionary
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">

    <!-- ========= Ultra-Slim Scrollbars ========= -->
    <SolidColorBrush x:Key=""ScrollbarTrackBrush"" Color=""#141820""/>
    <SolidColorBrush x:Key=""ScrollbarThumbBrush"" Color=""#2C3548""/>
    <SolidColorBrush x:Key=""ScrollbarThumbBrushHover"" Color=""#3A4663""/>
    <SolidColorBrush x:Key=""ScrollbarThumbBrushActive"" Color=""#4C5A7A""/>

    <Style x:Key=""SlimThumb"" TargetType=""{x:Type Thumb}"">
        <Setter Property=""Background"" Value=""{StaticResource ScrollbarThumbBrush}""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type Thumb}"">
                    <Border x:Name=""B"" Background=""{TemplateBinding Background}"" CornerRadius=""2""/>
                    <ControlTemplate.Triggers>
                        <Trigger Property=""IsMouseOver"" Value=""True"">
                            <Setter TargetName=""B"" Property=""Background"" Value=""{StaticResource ScrollbarThumbBrushHover}""/>
                        </Trigger>
                        <Trigger Property=""IsDragging"" Value=""True"">
                            <Setter TargetName=""B"" Property=""Background"" Value=""{StaticResource ScrollbarThumbBrushActive}""/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType=""{x:Type ScrollBar}"">
        <Setter Property=""Background"" Value=""Transparent""/>
        <Setter Property=""MinWidth"" Value=""0""/>
        <Setter Property=""MinHeight"" Value=""0""/>
        <Setter Property=""Template"">
            <Setter.Value>
                <ControlTemplate TargetType=""{x:Type ScrollBar}"">
                    <Grid SnapsToDevicePixels=""True"">
                        <Border Background=""{StaticResource ScrollbarTrackBrush}"" CornerRadius=""2""/>
                        <Track x:Name=""PART_Track""
                               Orientation=""{TemplateBinding Orientation}""
                               IsDirectionReversed=""True""
                               Focusable=""False"">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Opacity=""0"" IsHitTestVisible=""False""/>
                            </Track.DecreaseRepeatButton>
                            <Track.IncreaseRepeatButton>
                                <RepeatButton Opacity=""0"" IsHitTestVisible=""False""/>
                            </Track.IncreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb Style=""{StaticResource SlimThumb}""/>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>

        <Style.Triggers>
            <Trigger Property=""Orientation"" Value=""Vertical"">
                <Setter Property=""Width"" Value=""4""/>
                <Setter Property=""Margin"" Value=""0,2,2,2""/>
            </Trigger>
            <Trigger Property=""Orientation"" Value=""Horizontal"">
                <Setter Property=""Height"" Value=""4""/>
                <Setter Property=""Margin"" Value=""2,0,2,2""/>
            </Trigger>
        </Style.Triggers>
    </Style>
</ResourceDictionary>";

        // Dictionary aus XAML parsen
        var dict = (ResourceDictionary)XamlReader.Parse(xaml);

        // ScrollBar-Style aus dem Dictionary holen
        var sbStyle = (Style)dict[typeof(ScrollBar)];
        var thumbStyle = (Style)dict["SlimThumb"];

        // ins lokale Resource-Dict des Elements kippen
        target.Resources[typeof(ScrollBar)] = sbStyle;
        target.Resources["SlimThumb"] = thumbStyle;

        // sicherheitshalber Auto
        ScrollViewer.SetVerticalScrollBarVisibility(target, ScrollBarVisibility.Auto);
    }

    private void RefreshPathfinderPreviews()
    {
        if (_lastShops == null) return;
        if (_wantPreviewList == null || _payPreviewList == null) return;

        string wantTxt = _wantTb?.Text?.Trim() ?? "";
        string payTxt = _payTb?.Text?.Trim() ?? "";

        _wantPreviewList.Items.Clear();
        _payPreviewList.Items.Clear();

        // helper wie in RefreshShopSearchResults
        bool MatchGets(RustPlusClientReal.ShopOrder o, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return false;
            var pretty = ResolveItemName(o.ItemId, o.ItemShortName);
            return pretty.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool MatchPays(RustPlusClientReal.ShopOrder o, string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return false;
            var pretty = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName);
            return pretty.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // LEFT LIST: shops that SELL my wanted item
        if (!string.IsNullOrWhiteSpace(wantTxt))
        {
            foreach (var shop in _lastShops)
            {
                if (shop.Orders == null) continue;

                var matchingOffers = shop.Orders
                    .Where(o => o.Stock > 0 && MatchGets(o, wantTxt))
                    .ToList();

                if (matchingOffers.Count == 0) continue;

                _wantPreviewList.Items.Add(
                    BuildShopSearchCard(shop, matchingOffers, compact: true) // compact für Vorschau
                );
            }

            if (_wantPreviewList.Items.Count == 0)
            {
                _wantPreviewList.Items.Add(new TextBlock
                {
                    Text = "No direct seller for that item.",
                    Foreground = SearchText,
                    Opacity = 0.6
                });
            }
        }
        else
        {
            _wantPreviewList.Items.Add(new TextBlock
            {
                Text = "Type what you WANT to get.",
                Foreground = SearchText,
                Opacity = 0.4
            });
        }

        // RIGHT LIST: shops that ACCEPT what I can PAY
        if (!string.IsNullOrWhiteSpace(payTxt))
        {
            foreach (var shop in _lastShops)
            {
                if (shop.Orders == null) continue;

                var matchingOffers = shop.Orders
                    .Where(o => o.Stock > 0 && MatchPays(o, payTxt))
                    .ToList();

                if (matchingOffers.Count == 0) continue;

                _payPreviewList.Items.Add(
                    BuildShopSearchCard(shop, matchingOffers, compact: true)
                );
            }

            if (_payPreviewList.Items.Count == 0)
            {
                _payPreviewList.Items.Add(new TextBlock
                {
                    Text = "Nobody trades for that (as currency).",
                    Foreground = SearchText,
                    Opacity = 0.6
                });
            }
        }
        else
        {
            _payPreviewList.Items.Add(new TextBlock
            {
                Text = "Type what you CAN pay with.",
                Foreground = SearchText,
                Opacity = 0.4
            });
        }
    }

    // wir benutzen denselben Trick wie bei der Search-Leiste im ShopWindow:
    // eine gemeinsame Border mit Icon links + textbox rechts
    private FrameworkElement BuildRoundedSearchField(out TextBox tb, string iconEmoji, string placeholder)
    {
        var colOuterBg = Color.FromRgb(24, 26, 28);
        var colIconBg = Color.FromRgb(18, 20, 22);
        var colBorder = Color.FromArgb(160, 0, 173, 239);

        var outer = new Border
        {
            Background = new SolidColorBrush(colOuterBg),
            BorderBrush = new SolidColorBrush(colBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.IBeam
        };

        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconHost = new Border
        {
            Background = new SolidColorBrush(colIconBg),
            Padding = new Thickness(6, 4, 6, 4),
            IsHitTestVisible = false, // Klicks gehen durch
            Child = new TextBlock
            {
                Text = iconEmoji,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0)
            }
        };
        Grid.SetColumn(iconHost, 0);
        grid.Children.Add(iconHost);

        var localTb = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            CaretBrush = Brushes.White,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(160, 0, 173, 239)),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 160,
            FontSize = 12
        };
        tb = localTb;

        var placeholderBlock = new TextBlock
        {
            Text = placeholder,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            IsHitTestVisible = false,
            Opacity = 0.6
        };

        localTb.TextChanged += (s, e) =>
        {
            placeholderBlock.Visibility = string.IsNullOrEmpty(localTb.Text) ? Visibility.Visible : Visibility.Collapsed;
        };

        Grid.SetColumn(tb, 1);
        Grid.SetColumn(placeholderBlock, 1);

        grid.Children.Add(placeholderBlock);
        grid.Children.Add(tb);

        outer.Child = grid;
        outer.MouseDown += (s, e) => localTb.Focus();

        return outer;
    }

    // gleicher Style wie dein Profit-Trades Button-Knopf
    private Button MakeHeaderPillButton(string text)
    {
        return new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(14, 5, 14, 5),
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(35, 38, 41)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0, 173, 239)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Template = BuildRoundedButtonTemplate(),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        };
    }

    private ControlTemplate BuildRoundedButtonTemplate()
    {
        // abgerundet wie deine Accent-Buttons
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

        borderFactory.AppendChild(cp);
        template.VisualTree = borderFactory;

        // simple Hover Trigger
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(32, 34, 38)), "Bd"));

        // Pressed Trigger
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 173, 239)), "Bd"));
        pressed.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));

        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);

        return template;
    }

    private class PathStep
    {
        public RustPlusClientReal.ShopMarker Shop;
        public RustPlusClientReal.ShopOrder Order;

        public string FromItem = "";
        public string ToItem = "";

        public double PayAmount;
        public string PayPrettyName;
        public double GetAmount;
        public string GetPrettyName;

        public string FromKey;
        public string ToKey;
        public int PayItemId;
        public string PayShortName;
        public string GetShortName;
        public int GetItemId;
    }

    private class TradePathResult
    {
        public string OriginKey = "";       // der Startnode dieser Route
        public List<PathStep> Steps = new(); // in Reihenfolge
    }
    // Repräsentiert eine Handels-Kante: du gibst etwas, bekommst etwas.
    private class TradeEdge
    {
        public string PayShortNameRaw = ""; // o.CurrencyShortName
        public string GetShortNameRaw = ""; // o.ItemShortName
        // Graph-Knoten Keys (stabil, zum Routen)
        public string FromKey = "";   // "was du zahlst"-Knoten
        public string ToKey = "";   // "was du bekommst"-Knoten

        // Für die UI / Mengenanzeige
        public double PayAmount;         // CurrencyAmount
        public double GetAmount;         // Quantity
        public string PayPrettyName = "";
        public string GetPrettyName = "";

        public RustPlusClientReal.ShopMarker Shop;
        public RustPlusClientReal.ShopOrder Order;
    }


    private string NormalizePrettyForKey(string pretty)
    {
        // defensiv
        if (string.IsNullOrWhiteSpace(pretty))
            return "";

        // Beispiel:
        // "x1000 Cloth" -> "x1000_cloth"
        // "Sulfur Ore 400" -> "sulfur_ore_400"
        // "Metal Fragments" -> "metal_fragments"
        var s = pretty.Trim().ToLowerInvariant();

        // ersetze Whitespaces durch underscore
        s = System.Text.RegularExpressions.Regex.Replace(s, "\\s+", "_");

        // optional kannst du Sonderzeichen killen, damit Keys stabiler werden:
        s = System.Text.RegularExpressions.Regex.Replace(s, "[^a-z0-9_]+", "");

        return s;
    }

    private string? MakeItemKey(
        int itemId,
        string? shortName,
        string prettyName // <- NEU: wir geben jetzt den ResolveItemName()-Wert mit rein
    )
    {
        // 1. Echte ItemID (beste Variante, garantiert eindeutig)
        if (itemId > 0)
            return "id:" + itemId.ToString();

        // 2. ShortName aus Rust (zweitbeste Variante)
        if (!string.IsNullOrWhiteSpace(shortName))
            return "sn:" + shortName.Trim().ToLowerInvariant();

        // 3. Fallback auf den hübschen Anzeigenamen (damit Wood/Stone/etc. nicht verschwinden)
        if (!string.IsNullOrWhiteSpace(prettyName))
        {
            var norm = NormalizePrettyForKey(prettyName);
            if (!string.IsNullOrWhiteSpace(norm))
                return "pretty:" + norm;
        }

        // 4. gar nix brauchbares -> wir können keinen stabilen Knoten bauen
        return null;
    }
    private List<TradeEdge> BuildTradeGraphSnapshot()
    {
        var edges = new List<TradeEdge>();

        foreach (var shop in _lastShops)
        {
            if (shop.Orders == null) continue;

            foreach (var o in shop.Orders)
            {
                if (o.Stock <= 0) continue;
                if (o.CurrencyAmount <= 0 || o.Quantity <= 0) continue;

                string payPretty = ResolveItemName(o.CurrencyItemId, o.CurrencyShortName); // was man ZAHLT
                string getPretty = ResolveItemName(o.ItemId, o.ItemShortName);        // was man BEKOMMT

                if (string.IsNullOrWhiteSpace(payPretty)) continue;
                if (string.IsNullOrWhiteSpace(getPretty)) continue;

                // WICHTIG: wir geben jetzt payPretty/getPretty an MakeItemKey weiter
                string? fromKey = MakeItemKey(
                    o.CurrencyItemId,
                    o.CurrencyShortName,
                    payPretty
                );

                string? toKey = MakeItemKey(
                    o.ItemId,
                    o.ItemShortName,
                    getPretty
                );

                if (string.IsNullOrWhiteSpace(fromKey)) continue;
                if (string.IsNullOrWhiteSpace(toKey)) continue;
                if (string.Equals(fromKey, toKey, StringComparison.OrdinalIgnoreCase)) continue;

                edges.Add(new TradeEdge
                {
                    FromKey = fromKey,
                    ToKey = toKey,

                    PayShortNameRaw = o.CurrencyShortName ?? "",
                    GetShortNameRaw = o.ItemShortName ?? "",

                    PayPrettyName = payPretty,
                    GetPrettyName = getPretty,

                    PayAmount = o.CurrencyAmount,
                    GetAmount = o.Quantity,

                    Shop = shop,
                    Order = o
                });
            }
        }

        return edges;
    }


    // 1. helper für string matching
    private bool StrongMatch(string pretty, string raw, string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        if (!string.IsNullOrWhiteSpace(pretty))
        {
            if (pretty.Equals(user, StringComparison.OrdinalIgnoreCase)) return true;
            if (pretty.StartsWith(user, StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (raw.Equals(user, StringComparison.OrdinalIgnoreCase)) return true;
            if (raw.StartsWith(user, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool FuzzyMatch(string pretty, string raw, string user)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        if (!string.IsNullOrWhiteSpace(pretty) &&
            pretty.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (!string.IsNullOrWhiteSpace(raw) &&
            raw.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // akzeptiert auch "x1000 Stones", "Sulfur Ore", "Wood 500", etc.
    private bool LooseMatchName(string itemPretty, string itemAlt, string userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery)) return false;

        // normalize
        string uq = userQuery.Trim().ToLowerInvariant();

        // pretty check
        if (!string.IsNullOrWhiteSpace(itemPretty))
        {
            var p = itemPretty.Trim().ToLowerInvariant();
            if (p.Contains(uq)) return true;
        }

        // alt/fallback check
        if (!string.IsNullOrWhiteSpace(itemAlt))
        {
            var a = itemAlt.Trim().ToLowerInvariant();
            if (a.Contains(uq)) return true;
        }

        return false;
    }

    private bool FirstStepMatchesUser(List<PathStep> steps, string haveQ)
    {
        if (steps.Count == 0) return false;
        var first = steps[0];

        // wichtig: Wir schauen NICHT auf "FromItem" vs. "PayPrettyName" separat,
        // sondern matchen "was wir zahlen" locker gegen die User-Eingabe
        return LooseMatchName(first.PayPrettyName, first.FromItem, haveQ);
    }

    private bool LastStepMatchesUser(List<PathStep> steps, string wantQ)
    {
        if (steps.Count == 0) return false;
        var last = steps[steps.Count - 1];

        // gleiches Prinzip: was wir am Ende bekommen
        return LooseMatchName(last.GetPrettyName, last.ToItem, wantQ);
    }


    private List<TradePathResult> FindPathsItemToItem(
    string payItemQuery,   // RIGHT box: what I HAVE / will pay with
    string wantItemQuery,  // LEFT box: what I WANT to end up with
    int maxDepth)
    {
        var edges = BuildTradeGraphSnapshot();
        string haveQ = payItemQuery?.Trim() ?? "";
        string wantQ = wantItemQuery?.Trim() ?? "";

        AppendLog($"=== PATHFINDER RUN === haveQ='{haveQ}' wantQ='{wantQ}'");

        foreach (var e in edges)
        {
            bool payHit =
                StrongMatch(e.PayPrettyName, e.PayShortNameRaw, haveQ) ||
                FuzzyMatch(e.PayPrettyName, e.PayShortNameRaw, haveQ);

            bool getHit =
                StrongMatch(e.GetPrettyName, e.GetShortNameRaw, wantQ) ||
                FuzzyMatch(e.GetPrettyName, e.GetShortNameRaw, wantQ);

            if (payHit)
            {
              //  AppendLog($"START-CANDIDATE MATCH: haveQ='{haveQ}' matches Pay='{e.PayPrettyName}' raw='{e.PayShortNameRaw}' => FromKey={e.FromKey}");
            }

            if (getHit)
            {
              //  AppendLog($"TARGET-CANDIDATE MATCH: wantQ='{wantQ}' matches Get='{e.GetPrettyName}' raw='{e.GetShortNameRaw}' => ToKey={e.ToKey}");
            }
        }

       

        if (string.IsNullOrWhiteSpace(haveQ) || string.IsNullOrWhiteSpace(wantQ))
            return new List<TradePathResult>();

        // 1) mögliche Start-Knoten = Dinge, die du bezahlen kannst
        // helper wie unten beim finalen Filter, aber hier lokal ohne Order:
        bool LoosePayMatch(TradeEdge e, string userQ)
        {
            return LooseMatchName(e.PayPrettyName, e.PayShortNameRaw, userQ);
        }

        bool LooseGetMatch(TradeEdge e, string userQ)
        {
            return LooseMatchName(e.GetPrettyName, e.GetShortNameRaw, userQ);
        }

        // 1) mögliche Start-Knoten = Dinge, die du zahlen KANNST (RIGHT box)
        var startKeys = edges
            .Where(e => LoosePayMatch(e, haveQ))
            .Select(e => e.FromKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (startKeys.Count == 0)
            return new List<TradePathResult>();

        // 2) mögliche Ziel-Knoten = Dinge, die du HABEN WILLST (LEFT box)
        var targetKeys = edges
            .Where(e => LooseGetMatch(e, wantQ))
            .Select(e => e.ToKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (targetKeys.Count == 0)
            return new List<TradePathResult>();

        // --- 3) BFS ab JEDEM startKey (der Rest bleibt wie du ihn jetzt hast)
        var rawResults = new List<TradePathResult>();
       // AppendLog("startKeys:");
        foreach (var startKey in startKeys)
        {
         //   AppendLog("  " + startKey);
            var q = new Queue<(string curKey, List<PathStep> path)>();
            q.Enqueue((startKey, new List<PathStep>()));

            var visited = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [startKey] = 0
            };

            while (q.Count > 0)
            {
                var (curKey, curPath) = q.Dequeue();
                int depth = curPath.Count;
                if (depth >= maxDepth) continue;

                foreach (var edge in edges)
                {
                    // defensive guard
                    if (string.IsNullOrWhiteSpace(edge.FromKey)) continue;
                    if (!edge.FromKey.Equals(curKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var step = new PathStep
                    {
                        Shop = edge.Shop,
                        Order = edge.Order,

                        FromItem = edge.PayPrettyName,   // what we PAY this step
                        ToItem = edge.GetPrettyName,   // what we GET this step

                        PayAmount = edge.PayAmount,
                        PayPrettyName = edge.PayPrettyName,
                        GetAmount = edge.GetAmount,
                        GetPrettyName = edge.GetPrettyName,

                        FromKey = edge.FromKey,
                        ToKey = edge.ToKey
                    };

                    var newPath = new List<PathStep>(curPath) { step };
                    var newKey = edge.ToKey;
                    int newDepth = newPath.Count;

                    // haben wir Ziel getroffen?
                    if (targetKeys.Contains(newKey))
                    {
                        rawResults.Add(new TradePathResult
                        {
                            OriginKey = startKey,
                            Steps = newPath
                        });
                    }

                    if (!visited.TryGetValue(newKey, out var oldDepth) || newDepth < oldDepth)
                    {
                        visited[newKey] = newDepth;
                        q.Enqueue((newKey, newPath));
                    }
                }
            }
        }

        // --- 4) Final filtern & deduplizieren -----------------------------

        // Hilfsfunktionen: prüft, ob der erste Step wirklich mit dem zahlt,
        // was der User rechts eingegeben hat (haveQ),
        // und ob der letzte Step wirklich das ausliefert,
        // was der User links eingegeben hat (wantQ).

        bool FirstStepMatchesUser(List<PathStep> steps, string have)
        {
            if (steps.Count == 0) return false;
            var first = steps[0];

            // was du im ersten Schritt BEZAHLST soll ungefähr dem entsprechen,
            // was du rechts eingetippt hast
            return LooseMatchName(first.PayPrettyName, first.Order?.CurrencyShortName ?? "", have);
        }

        bool LastStepMatchesUser(List<PathStep> steps, string want)
        {
            if (steps.Count == 0) return false;
            var last = steps[steps.Count - 1];

            // was du am Ende BEKOMMST soll ungefähr dem entsprechen,
            // was du links eingetippt hast
            return LooseMatchName(last.GetPrettyName, last.Order?.ItemShortName ?? "", want);
        }

        // echte Filterung
        var filtered = rawResults
            .Where(r => FirstStepMatchesUser(r.Steps, haveQ)
                     && LastStepMatchesUser(r.Steps, wantQ))
            .ToList();

        // Dedup nach (OriginKey -> letzter Node + Länge)
        var dedup = new Dictionary<string, TradePathResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in filtered)
        {
            if (r.Steps.Count == 0) continue;
            var lastStep = r.Steps[r.Steps.Count - 1];

            string sig = $"{r.OriginKey}->{lastStep.ToKey}#{r.Steps.Count}";
            if (!dedup.ContainsKey(sig))
            {
                dedup[sig] = r;
            }
        }

        // Optionaler Mini-Filter gegen Frankenstein-Pfade:
        // Kill Pfade, in denen ein Mittelschritt zahlt mit etwas,
        // was er gar nicht direkt vorher bekommen hat ODER was nicht die Startwährung war.
        // (Das unterbindet "kaufe L96 Rifle nur damit du sie NIE einsetzt".)
        bool LooksCoherent(TradePathResult p)
        {
            if (p.Steps.Count == 0) return false;

            // wir tracken "was habe ich nach jedem Schritt im Inventar"
            // Start: wir haben nur die erste Währung
            var haveSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            haveSet.Add(p.Steps[0].PayPrettyName); // Startwährung

            foreach (var st in p.Steps)
            {
                // um st zu bezahlen musst du st.PayPrettyName besitzen
                if (!haveSet.Contains(st.PayPrettyName))
                    return false;

                // nach dem Kauf besitzt du außerdem st.GetPrettyName
                haveSet.Add(st.GetPrettyName);
            }

            return true;
        }

        var coherent = dedup.Values
            .Where(LooksCoherent)
            .ToList();

        return coherent;


    }


    private class ItemInfo2
    {
        public string Pretty = "";
        public string Raw = "";
    }

    private Dictionary<string, ItemInfo2> BuildItemDictionary(List<TradeEdge> edges)
    {
        var dict = new Dictionary<string, ItemInfo2>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string pretty, string raw)
        {
            if (!dict.ContainsKey(key))
            {
                dict[key] = new ItemInfo2 { Pretty = pretty, Raw = raw };
            }
        }

        foreach (var e in edges)
        {
            Add(e.FromKey, e.PayPrettyName, e.PayShortNameRaw);
            Add(e.ToKey, e.GetPrettyName, e.GetShortNameRaw);
        }

        return dict;
    }

    //BERECHNUNG DER MIN- / MAX:

    private class PathRunSummary
    {
        public string StartName = "";
        public string FinalName = "";
        public double[] MinRuns = Array.Empty<double>(); // length = steps
        public double MinStartCost;   // Kosten für die kleinste sinnvolle Kette (mind. 1x Final)
        public double MinFinalGain;   // Output der kleinsten Kette
        public double[] MaxRuns = Array.Empty<double>();
        public double MaxStartCost;   // Kosten bei Bottleneck-Max
        public double MaxFinalGain;   // Output bei Bottleneck-Max
        public double DroneCost;      // Steps * 20
        public double DroneCostMin;   // Steps * 20
        public double DroneCostMax;   // Steps * 20

        public List<(int stepIndex, double runs)> RunsByStep = new();
        public List<(int stepIndex, double runs)> MinRunsByStep = new(); // <-- NEU
        public Dictionary<string, double> Leftovers = new(StringComparer.OrdinalIgnoreCase);
        public bool MinChainFeasible;
        public List<string> Blockers = new();
    }

    // nimmt deinen fertigen Pfad (Steps in Reihenfolge) und rechnet min/max
    private PathRunSummary? ComputePathRunSummaryStrict(TradePathResult path)
    {
        var steps = path.Steps;
        if (steps.Count == 0) return null;

        var sum = new PathRunSummary();
        var first = steps[0];
        var last = steps[^1];

        sum.StartName = first.PayPrettyName ?? "";
        sum.FinalName = last.GetPrettyName ?? "";

        int n = steps.Count;

        // convenient arrays
        var stock = new double[n];
        var payAmt = new double[n];
        var getAmt = new double[n];
        var payNam = new string[n];
        var getNam = new string[n];

        for (int i = 0; i < n; i++)
        {
            var st = steps[i];
            stock[i] = st.Order?.Stock ?? 0;
            payAmt[i] = st.PayAmount;
            getAmt[i] = st.GetAmount;
            payNam[i] = st.PayPrettyName ?? "";
            getNam[i] = st.GetPrettyName ?? "";
        }

        // --- MIN CHAIN ---
        // Force 1 run of the LAST step (buy exactly one final order).
        // Then back-propagate required runs for previous steps via ceil().
        var minRuns = new double[n];
        minRuns[^1] = 1;

        for (int i = n - 2; i >= 0; i--)
        {
            if (getAmt[i] <= 0) return null; // broken offer
                                             // produce enough output for the next step’s total pay
            double needOutNext = minRuns[i + 1] * payAmt[i + 1];
            double req = Math.Ceiling(needOutNext / getAmt[i]);

            // must be positive and within stock
            if (req <= 0 || req > stock[i]) return null; // bottleneck => reject entire path
            minRuns[i] = req;
        }

        // Forward sanity: each step i must have enough input from i-1
        for (int i = 1; i < n; i++)
        {
            double producedPrev = minRuns[i - 1] * getAmt[i - 1];
            double needForThis = minRuns[i] * payAmt[i];
            if (needForThis > producedPrev) return null; // inconsistency => reject
        }

        // MIN numbers are valid
        sum.MinRuns = minRuns;
        sum.MinStartCost = minRuns[0] * payAmt[0];
        sum.MinFinalGain = minRuns[^1] * getAmt[^1];

        // --- MAX CHAIN ---
        // Start optimistic: each step could run at its stock,
        // then iteratively clamp to keep producer/consumer consistent.
        var runs = new double[n];
        for (int i = 0; i < n; i++) runs[i] = stock[i];

        for (int iter = 0; iter < 12; iter++)
        {
            bool changed = false;

            // backward: earlier steps must produce enough for later steps
            for (int i = n - 2; i >= 0; i--)
            {
                double needOutNext = runs[i + 1] * payAmt[i + 1];
                double canPerRun = getAmt[i];
                double reqRunsI = canPerRun > 0 ? Math.Ceiling(needOutNext / canPerRun) : 0;

                if (reqRunsI < 0) reqRunsI = 0;
                if (reqRunsI > stock[i]) reqRunsI = stock[i];

                if (Math.Abs(runs[i] - reqRunsI) > 0.0001)
                {
                    runs[i] = reqRunsI;
                    changed = true;
                }
            }

            // forward: later steps cannot consume more than previous produce
            for (int i = 1; i < n; i++)
            {
                double producedPrev = runs[i - 1] * getAmt[i - 1];
                double maxRunsI = payAmt[i] > 0 ? Math.Floor(producedPrev / payAmt[i]) : 0;
                if (maxRunsI < 0) maxRunsI = 0;
                if (maxRunsI > stock[i]) maxRunsI = stock[i];

                if (runs[i] > maxRunsI + 0.0001)
                {
                    runs[i] = maxRunsI;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        sum.MaxRuns = runs;
        sum.MaxStartCost = runs[0] * payAmt[0];
        sum.MaxFinalGain = runs[^1] * getAmt[^1];

        // Drone costs are PER STEP, not per trade
        sum.DroneCostMin = n * 20.0;
        sum.DroneCostMax = n * 20.0;

        // Leftovers for MAX case (nice to show)
        var leftovers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n - 1; i++)
        {
            double produced = runs[i] * getAmt[i];
            double consumed = runs[i + 1] * payAmt[i + 1];
            double lf = produced - consumed;
            if (lf > 0.0001)
            {
                string key = getNam[i];
                leftovers[key] = leftovers.TryGetValue(key, out var v) ? v + lf : lf;
            }
        }
        foreach (var kv in leftovers)
            if (!kv.Key.Equals(sum.FinalName, StringComparison.OrdinalIgnoreCase))
                sum.Leftovers[kv.Key] = kv.Value;

        return sum;
    }


    private FrameworkElement? BuildPathCard(TradePathResult path)
    {

        var summary = ComputePathRunSummaryStrict(path);
        if (summary == null) return null; // reject whole path (true bottleneck)

        var outer = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(24, 26, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outer.Child = grid;

        // LEFT
        var left = new StackPanel { Orientation = Orientation.Vertical };
        left.Children.Add(new TextBlock
        {
            Text = $"Get {summary.FinalName} starting from {summary.StartName} in {path.Steps.Count} step(s)",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.WrapWithOverflow
        });

        // Steps with "Current stock" and "Min to reach goal: xN"
        for (int i = 0; i < path.Steps.Count; i++)
            left.Children.Add(BuildStepRowWithMin(i + 1, path.Steps[i], summary.MinRuns[i]));

        Grid.SetColumn(left, 0);
        grid.Children.Add(left);


        // RIGHT
        var summaryBox = BuildSummaryBoxStrict(summary, path);
        grid.Children.Add(summaryBox);
        Grid.SetColumn(summaryBox, 1);

        return outer;
    }

    // helper to keep earlier style
    private FrameworkElement BuildStepRowWithMin(int idx, PathStep step, double minRunsForStep)
    {
        var rowOuter = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowOuter.Child = row;

        var left = new StackPanel { Orientation = Orientation.Vertical};

        // Zeile mit Icons „pay → get“
        left.Children.Add(new TextBlock
        {
            Text = $"Step {idx}:",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        left.Children.Add(BuildPayGetRow(step));  // <- deine vorhandene Icon-Zeile

        // Zusatzinfos
        left.Children.Add(new TextBlock { Text = $"Current stock: {step.Order?.Stock ?? 0}", Foreground = SearchText, FontSize = 11, Opacity = 0.85 });
        left.Children.Add(new TextBlock { Text = $"Min to reach goal: x{Math.Max(1, Math.Floor(minRunsForStep))}", Foreground = SearchText, FontSize = 11, Opacity = 0.85 });
        left.Children.Add(new TextBlock { Text = $"{(step.Shop.Label ?? "Shop")} [{GetGridLabel(step.Shop)}]", Foreground = SearchText, FontSize = 11, Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 });

        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        var goBtn = MakeHeaderPillButton("Go");
        goBtn.Margin = new Thickness(8, 0, 0, 0);
        goBtn.Click += (_, __) => CenterMapOnWorld(step.Shop.X, step.Shop.Y);
        Grid.SetColumn(goBtn, 1);
        row.Children.Add(goBtn);

        return rowOuter;
    }

    private static Border UiSeparator(double top = 6, double bottom = 6)
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Margin = new Thickness(0, top, 0, bottom)
        };
    }

    // "Icon + xMenge" – nutzt DEIN bestehendes BindIcon.
    private FrameworkElement IconWithQty(string? shortName, int itemId, double qty,
                                     double iconSize = 24, double fontSize = 13, bool bold = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var img = new Image { Width = iconSize, Height = iconSize, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(img, shortName, itemId); // <- deine vorhandene Funktion

        var txt = new TextBlock
        {
            Text = $"x{Math.Floor(qty)}",
            Foreground = SearchText,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Children.Add(img);
        row.Children.Add(txt);
        return row;
    }

    private FrameworkElement BuildSummaryBoxStrict(PathRunSummary sum, TradePathResult path)
    {
        var first = path.Steps.First();
        var last = path.Steps.Last();

        // Fallbacks falls Order null sein sollte
        string? startShort = first.Order?.CurrencyShortName ?? first.PayPrettyName;
        int startId = first.Order?.CurrencyItemId ?? 0;

        string? finalShort = last.Order?.ItemShortName ?? last.GetPrettyName;
        int finalId = last.Order?.ItemId ?? 0;

        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 260
        };

        var st = new StackPanel { Orientation = Orientation.Vertical };
        box.Child = st;

        // Titel
        st.Children.Add(new TextBlock
        {
            Text = "Max @ current stock:",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        });

        // Große Symbolzeile: [Pay xN] -> [Get xN]
        var big = new Grid { Margin = new Thickness(0, 4, 0, 6) };
        big.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        big.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        big.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var payIcon = IconWithQty(startShort, startId, sum.MaxStartCost, 24, 13, true);
        Grid.SetColumn(payIcon, 0); big.Children.Add(payIcon);

        var arrow = new TextBlock { Text = "  →  ", Foreground = SearchText, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(arrow, 1); big.Children.Add(arrow);

        var getIcon = IconWithQty(finalShort, finalId, sum.MaxFinalGain, 24, 13, true);
        Grid.SetColumn(getIcon, 2); big.Children.Add(getIcon);

        st.Children.Add(big);

        // Klartext-Zeile
        st.Children.Add(new TextBlock
        {
            Text = $"Get {Math.Floor(sum.MaxFinalGain)} {sum.FinalName} for {Math.Floor(sum.MaxStartCost)} {sum.StartName}",
            Foreground = SearchText,
            FontSize = 12
        });

        // Leftovers
        if (sum.Leftovers.Count > 0)
        {
            var leftStr = string.Join(", ", sum.Leftovers.Select(k => $"{Math.Floor(k.Value)} {k.Key}"));
            st.Children.Add(new TextBlock
            {
                Text = $"Leftovers: {leftStr}",
                Foreground = SearchText,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        // Drone costs – nimm DroneCostMax, sonst auf DroneCost/n*20 zurückfallen
        double drone = sum.DroneCostMax > 0 ? sum.DroneCostMax : (sum.DroneCost > 0 ? sum.DroneCost : path.Steps.Count * 20);
        st.Children.Add(new TextBlock
        {
            Text = $"Drone costs: {Math.Floor(drone)} Scrap",
            Foreground = SearchText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });

        st.Children.Add(UiSeparator());

        // Aggregierte Step-Totals für MAX-Plan
        for (int i = 0; i < path.Steps.Count; i++)
        {
            var step = path.Steps[i];
            double r = (sum.MaxRuns != null && i < sum.MaxRuns.Length) ? sum.MaxRuns[i] : 0.0;
            if (r <= 0) continue;

            double totalPay = r * step.PayAmount;
            double totalGet = r * step.GetAmount;

            st.Children.Add(new TextBlock
            {
                Text = $"Step {i + 1}: Pay {Math.Floor(totalPay)} {step.PayPrettyName} → Get {Math.Floor(totalGet)} {step.GetPrettyName}",
                Foreground = SearchText,
                FontSize = 11
            });
        }

        st.Children.Add(UiSeparator());

        // Min-Chain ganz unten
        var minLine = $"Min chain: Pay {Math.Floor(sum.MinStartCost)} {sum.StartName} → Get {Math.Floor(sum.MinFinalGain)} {sum.FinalName}";
        if (!sum.MinChainFeasible && (sum.Blockers?.Count > 0))
            minLine += "  (not feasible at current stock)";

        st.Children.Add(new TextBlock { Text = minLine, Foreground = SearchText, FontSize = 11 });

        if (!sum.MinChainFeasible && (sum.Blockers?.Count > 0))
        {
            st.Children.Add(new TextBlock
            {
                Text = "Bottleneck: " + sum.Blockers[0],
                Foreground = SearchText,
                FontSize = 11,
                Opacity = 0.9
            });
        }

        return box;
    }


    private FrameworkElement BuildSummaryBox(PathRunSummary sum, TradePathResult path)
    {
        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 44, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 220
        };

        var st = new StackPanel { Orientation = Orientation.Vertical };
        box.Child = st;

        st.Children.Add(new TextBlock
        {
            Text = "Max @ current stock:",
            Foreground = SearchText,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12
        });
       

        st.Children.Add(new TextBlock
        {
            Text = $"Get {Math.Floor(sum.MaxFinalGain)} {sum.FinalName} for {Math.Floor(sum.MaxStartCost)} {sum.StartName}",
            Foreground = SearchText,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 4)
        });

        st.Children.Add(new TextBlock
        {
            Text = $"Drone costs: {Math.Floor(sum.DroneCost)} Scrap",
            Foreground = SearchText,
            FontSize = 11
        });

        if (sum.Leftovers.Count > 0)
        {
            var leftoverStrs = sum.Leftovers
                .Select(kvp => $"{Math.Floor(kvp.Value)} {kvp.Key}");

            st.Children.Add(new TextBlock
            {
                Text = "Leftovers: " + string.Join(", ", leftoverStrs),
                Foreground = SearchText,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 4)
            });
        }
        else
        {
            st.Children.Add(new TextBlock
            {
                Text = "",
                FontSize = 4
            });
        }

        // Detail-Zeilen pro Step mit aggregierten Mengen
        var steps = path.Steps;
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var (idx, r) = sum.RunsByStep[i]; // idx == i, r == runs[i]

            if (r <= 0) continue; // wenn wir den Step wirklich gar nicht fahren, skippen

            double totalPay = r * step.PayAmount;
            double totalGet = r * step.GetAmount;

            st.Children.Add(new TextBlock
            {
                Text = $"Step {i + 1}: Pay {Math.Floor(totalPay)} {step.PayPrettyName} → Get {Math.Floor(totalGet)} {step.GetPrettyName}",
                Foreground = SearchText,
                FontSize = 11
            });
            if (sum.MinFinalGain > 0)
            {
                var minLine = $"Min chain: Pay {Math.Floor(sum.MinStartCost)} {sum.StartName} → Get {Math.Floor(sum.MinFinalGain)} {sum.FinalName}";
                if (!sum.MinChainFeasible && sum.Blockers.Count > 0)
                    minLine += "  (not feasible at current stock)";

                

                if (!sum.MinChainFeasible && sum.Blockers.Count > 0)
                {
                    st.Children.Add(new TextBlock
                    {
                        Text = "Bottleneck: " + sum.Blockers[0],
                        Foreground = SearchText,
                        FontSize = 11,
                        Opacity = 0.9
                    });
                }
            }
        }
        var minLine2 = $"Min chain: Pay {Math.Floor(sum.MinStartCost)} {sum.StartName} → Get {Math.Floor(sum.MinFinalGain)} {sum.FinalName}";
        st.Children.Add(new TextBlock
        {
            Text = minLine2,
            Foreground = SearchText,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 2)
        });

        return box;
    }

    private FrameworkElement BuildStepRow(int idx, PathStep step, PathRunSummary summary)
    {
        var rowOuter = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            Margin = new Thickness(0, 4, 0, 0)
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rowOuter.Child = row;

        // LEFT
        var leftStack = new StackPanel { Orientation = Orientation.Vertical };

        // Zeile mit Icons „pay → get“
        leftStack.Children.Add(BuildPayGetRow(step));

        // Current stock
        leftStack.Children.Add(new TextBlock
        {
            Text = $"Current stock: {step.Order?.Stock ?? 0}",
            Foreground = SearchText,
            FontSize = 11,
            Opacity = 0.85
        });

        // Min xN (aus MIN-Summary)
        var minPair = summary.MinRunsByStep.FirstOrDefault(t => t.stepIndex == (idx - 1));
        if (minPair.runs > 0)
        {
            leftStack.Children.Add(new TextBlock
            {
                Text = $"Min to reach goal: x{minPair.runs}",
                Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                FontSize = 11
            });
        }

        // Shop Info
        leftStack.Children.Add(new TextBlock
        {
            Text = $"{(step.Shop.Label ?? "Shop")} [{GetGridLabel(step.Shop)}]",
            Foreground = SearchText,
            FontSize = 12,
            Opacity = 0.8
        });

        Grid.SetColumn(leftStack, 0);
        row.Children.Add(leftStack);

        // RIGHT: Go
        var goBtn = MakeHeaderPillButton("Go");
        goBtn.Margin = new Thickness(8, 0, 0, 0);
        goBtn.Click += (_, __) => CenterMapOnWorld(step.Shop.X, step.Shop.Y);

        Grid.SetColumn(goBtn, 1);
        row.Children.Add(goBtn);

        return rowOuter;
    }

    private FrameworkElement BuildPayGetRow(PathStep st)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var payIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(payIcon, st.Order?.CurrencyShortName, st.Order?.CurrencyItemId ?? 0);
        g.Children.Add(payIcon);

        var payTxt = new TextBlock
        {
            Text = $"Pay {st.PayAmount} {st.PayPrettyName}",
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(payTxt, 1);
        g.Children.Add(payTxt);

        var arrow = new TextBlock
        {
            Text = "  →  ",
            Margin = new Thickness(6, 0, 6, 0),
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        g.Children.Add(arrow);

        var getIcon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) };
        BindIcon(getIcon, st.Order?.ItemShortName, st.Order?.ItemId ?? 0);
        Grid.SetColumn(getIcon, 3);
        g.Children.Add(getIcon);

        var getTxt = new TextBlock
        {
            Text = $"Get {st.GetAmount} {st.GetPrettyName}",
            Foreground = SearchText,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(getTxt, 4);
        g.Children.Add(getTxt);

        return g;
    }

    private void RunPathAnalysis()
    {
        if (_pathResultList == null) return;

        string wantTxt = _wantTb?.Text ?? "";
        string payTxt = _payTb?.Text ?? "";

        int depth = 4;
       // if (_depthCb?.SelectedIndex == 0) depth = 2;
      //  else if (_depthCb?.SelectedIndex == 1) depth = 3;
      //  else if (_depthCb?.SelectedIndex == 2) depth = 4;

        _pathResultList.Items.Clear();
        _pathResultList.Items.Add(new TextBlock
        {
            Text = "Searching...",
            Foreground = SearchText
        });

        // aktuell synchron, was bei deiner Scale noch okay ist
        var paths = FindPathsItemToItem(payTxt, wantTxt, depth);

        _pathResultList.Items.Clear();

        if (paths.Count == 0)
        {
            _pathResultList.Items.Add(new TextBlock
            {
                Text = "No route found.",
                Foreground = SearchText
            });
            return;
        }

        // Sort shortest first, keep up to 30, but only those that pass strict summary
        int shown = 0;
        foreach (var p in paths.OrderBy(p => p.Steps.Count))
        {
            var card = BuildPathCard(p);
            if (card != null)
            {
                _pathResultList.Items.Add(card);
                if (++shown >= 30) break;
            }
        }
        if (shown == 0)
        {
            _pathResultList.Items.Add(new TextBlock { Text = "No valid route (bottlenecks).", Foreground = SearchText });
        }
    }

    private ListBox? _analysisListBox;
    private bool _profitTradesInitialized;
    private void OpenAnalysisWindow()
    {
        if (ProfitTradesPanel.Visibility == Visibility.Visible)
        {
            ProfitTradesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        BuyXForYPanel.Visibility = Visibility.Collapsed;

        if (!_profitTradesInitialized)
        {
            _profitTradesInitialized = true;
            _analysisListBox = ProfitTradesList;
            BtnRefreshProfitTrades.Click += (_, __) => RefreshAnalysisWindow();
            BtnCloseProfitTrades.Click += (_, __) => ProfitTradesPanel.Visibility = Visibility.Collapsed;
        }

        ProfitTradesPanel.Visibility = Visibility.Visible;
        RefreshAnalysisWindow();
        UpdateShopSearchToolHighlights();
    }
    private void RefreshAnalysisWindow()
    {
        if (_analysisListBox == null) return;

        _analysisListBox.Items.Clear();

        if (_lastShops == null || _lastShops.Count == 0)
        {
            _analysisListBox.Items.Add(new TextBlock
            {
                Text = "No shop data yet.",
                Foreground = SearchText
            });
            return;
        }

        var flips = FindTwoStepFlips(_lastShops);

        _analysisListBox.Items.Add(new TextBlock
        {
            Text = "Possible 2-step profit loops",
            Foreground = SearchText,
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (flips.Count == 0)
        {
            _analysisListBox.Items.Add(new TextBlock
            {
                Text = "No profitable flips found.",
                Foreground = SearchText
            });
            return;
        }

        int shown = 0;
        foreach (var flip in flips)
        {
            _analysisListBox.Items.Add(BuildFlipCard(flip));
            if (++shown >= 20) break;
        }
    }

    private ListBox? _analysisList;             // Ergebnisse der Analyse
}
