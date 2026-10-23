using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustPlusDesk.Helpers;
using RustPlusDesk.Services.AiCompanion;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views.Windows
{
    /// <summary>
    /// A first-run walk through the AI companion: what it does and where recordings go, which
    /// provider to pick, how to get a key from that provider, a live test of the key, and the
    /// two or three choices that decide how it is used in game.
    ///
    /// The settings page assumes someone already knows what an API key is and where to find
    /// one. Most people do not, and the usual failure is quiet — a key from the wrong page, an
    /// account with no credit, a provider switched without a key — so this window does each
    /// step with the instructions beside it and proves the key works before letting go.
    ///
    /// Everything is saved as it is chosen, through the same store the settings page uses, so
    /// closing half way keeps whatever was finished.
    /// </summary>
    public partial class AiCompanionSetupWindow : WpfUi.FluentWindow
    {
        private enum Step { Welcome, Provider, Key, Test, Finish }

        private static readonly Step[] Steps = { Step.Welcome, Step.Provider, Step.Key, Step.Test, Step.Finish };

        private Step _step = Step.Welcome;
        private string _provider;
        private bool _testPassed;
        private bool _testing;
        private bool _loading;
        private int _testRun;
        private CancellationTokenSource? _testCancel;

        public AiCompanionSetupWindow()
        {
            InitializeComponent();

            var settings = AiCompanionStore.Current;

            // Start on the provider already chosen if it has a key, otherwise on the one that
            // costs nothing to try: a new user should not have to find a card to see it work.
            _provider = AiCompanionStore.HasKeyFor(settings.Provider) ? settings.Provider : AiProviders.Gemini;

            ApplyStaticText();
            BuildWelcome();
            BuildProviderCards();

            _loading = true;
            ChkPolicy.IsChecked = settings.PolicyAcceptedUtc != null;
            ChkTextAnswers.IsChecked = settings.TextAnswers;
            ChkVoiceAnswers.IsChecked = settings.AudioAnswers;
            _loading = false;

            Closed += (_, _) => CancelTest();

            ShowStep(Step.Welcome);
        }

        // ------------------------------------------------------------------ navigation

        private void ShowStep(Step step)
        {
            _step = step;

            StepWelcome.Visibility = step == Step.Welcome ? Visibility.Visible : Visibility.Collapsed;
            StepProvider.Visibility = step == Step.Provider ? Visibility.Visible : Visibility.Collapsed;
            StepKey.Visibility = step == Step.Key ? Visibility.Visible : Visibility.Collapsed;
            StepTest.Visibility = step == Step.Test ? Visibility.Visible : Visibility.Collapsed;
            StepFinish.Visibility = step == Step.Finish ? Visibility.Visible : Visibility.Collapsed;

            switch (step)
            {
                case Step.Provider: RefreshProviderCards(); break;
                case Step.Key: BuildKeyStep(); break;
                case Step.Test: BuildTestStep(); break;
                case Step.Finish: BuildFinishStep(); break;
            }

            ContentScroll.ScrollToTop();
            BuildRail();
            UpdateFooter();

            // The test runs itself the first time it is reached: the user has just pasted a
            // key and the next thing they want to know is whether it works.
            if (step == Step.Test && !_testPassed && !_testing) _ = RunTestAsync();
        }

        private void UpdateFooter()
        {
            BtnBack.Content = T("AiSetupBack", "Back");
            BtnBack.Visibility = _step == Step.Welcome ? Visibility.Collapsed : Visibility.Visible;

            BtnNext.Content = _step switch
            {
                Step.Finish => T("AiSetupDone", "Done"),
                Step.Test when !_testPassed => T("AiSetupSkipTest", "Continue anyway"),
                _ => T("AiSetupNext", "Next"),
            };

            BtnNext.Appearance = _step == Step.Test && !_testPassed
                ? WpfUi.ControlAppearance.Secondary
                : WpfUi.ControlAppearance.Primary;

            BtnNext.IsEnabled = _step switch
            {
                Step.Welcome => ChkPolicy.IsChecked == true,
                Step.Key => KeyStepComplete(),
                Step.Test => !_testing,
                _ => true,
            };

            TxtFooterHint.Text = _step switch
            {
                Step.Welcome when ChkPolicy.IsChecked != true =>
                    T("AiSetupFooterPolicy", "Tick the box above to continue."),
                Step.Key when !KeyStepComplete() =>
                    T("AiSetupFooterKey", "Paste your key to continue."),
                Step.Test when _testing => T("AiSetupFooterTesting", "Waiting for the provider…"),
                _ => string.Format(T("AiSetupFooterStep", "Step {0} of {1}"),
                    Array.IndexOf(Steps, _step) + 1, Steps.Length),
            };
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            switch (_step)
            {
                case Step.Welcome:
                    if (ChkPolicy.IsChecked != true) return;
                    var settings = AiCompanionStore.Current;
                    if (settings.PolicyAcceptedUtc == null)
                    {
                        settings.PolicyAcceptedUtc = DateTime.UtcNow;
                        AiCompanionStore.Save(settings);
                    }
                    ShowStep(Step.Provider);
                    break;

                case Step.Provider:
                    SaveProvider();
                    ShowStep(Step.Key);
                    break;

                case Step.Key:
                    if (!SaveKey()) return;
                    ShowStep(Step.Test);
                    break;

                case Step.Test:
                    ShowStep(Step.Finish);
                    break;

                case Step.Finish:
                    DialogResult = true;
                    Close();
                    break;
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            CancelTest();

            int index = Array.IndexOf(Steps, _step);
            if (index > 0) ShowStep(Steps[index - 1]);
        }

        /// <summary>
        /// Steps down the left side: done ones ticked and clickable, the current one lit, the
        /// rest dim. Going forward by clicking is not offered — each step guards the next.
        /// </summary>
        private void BuildRail()
        {
            StepRail.Children.Clear();
            int current = Array.IndexOf(Steps, _step);

            for (int i = 0; i < Steps.Length; i++)
            {
                var step = Steps[i];
                bool done = i < current;
                bool active = i == current;

                var dot = new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(0, 0, 10, 0),
                    BorderThickness = new Thickness(1.5),
                    BorderBrush = active || done ? Brush("Accent") : Brush("CardBorder"),
                    Background = active ? Brush("Accent") : Brushes.Transparent,
                    Child = done
                        ? new WpfUi.SymbolIcon
                        {
                            Symbol = WpfUi.SymbolRegular.Checkmark16,
                            FontSize = 13,
                            Foreground = Brush("Accent"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        }
                        : new TextBlock
                        {
                            Text = (i + 1).ToString(),
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = active ? Brush("AppBg") : Brush("TextSubtle"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                };

                var label = new TextBlock
                {
                    Text = StepName(step),
                    FontSize = 12,
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = active ? Brush("TextPrimary") : Brush("TextSubtle"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(dot);
                row.Children.Add(label);

                var hit = new Border
                {
                    Padding = new Thickness(6, 5, 6, 5),
                    CornerRadius = new CornerRadius(6),
                    Background = active ? Brush("SurfaceAlt") : Brushes.Transparent,
                    Child = row,
                    Opacity = i > current ? 0.6 : 1.0,
                };

                if (done)
                {
                    hit.Cursor = System.Windows.Input.Cursors.Hand;
                    hit.MouseLeftButtonUp += (_, _) => { CancelTest(); ShowStep(step); };
                }

                StepRail.Children.Add(hit);
            }

            TxtRailFoot.Text = T("AiSetupRailFoot",
                "Your key stays on this PC. You can rerun this setup any time from Settings › AI Companion.");
        }

        private static string StepName(Step step) => step switch
        {
            Step.Welcome => T("AiSetupStepWelcome", "Welcome"),
            Step.Provider => T("AiSetupStepProvider", "Choose a provider"),
            Step.Key => T("AiSetupStepKey", "Get your API key"),
            Step.Test => T("AiSetupStepTest", "Test the connection"),
            _ => T("AiSetupStepFinish", "Finish"),
        };

        // ------------------------------------------------------------------ 1. welcome

        private void ApplyStaticText()
        {
            var title = T("AiSetupWindowTitle", "AI Companion setup");
            Title = title;
            TitleBarMain.Title = title;
            TxtRailTitle.Text = T("AiCompanionTitle", "AI Companion");
            TxtRailSubtitle.Text = T("AiSetupRailSubtitle", "Guided setup · about 3 minutes");

            TxtWelcomeTitle.Text = T("AiSetupWelcomeTitle", "Ask Rust questions without leaving the game");
            TxtWelcomeSub.Text = T("AiSetupWelcomeSub",
                "The companion uses an AI model from a provider you pick, through your own account with them. " +
                "This guide gets you a key, checks it works and sets up the hotkey. You only need a web browser.");

            TxtPrivacyTitle.Text = T("AiSetupPrivacyTitle", "Your account, your key — not ours");
            TxtPrivacyBody.Text = T("AiSetupPrivacyBody",
                "Questions, recordings and screenshots go straight from this PC to the provider you choose. " +
                "They never pass through our servers. The provider's own terms apply to them.\n" +
                "Your key is stored only on this PC, protected by your Windows account, and is never uploaded. " +
                "Recording only runs while you hold the hotkey or after you click the tile, and stops by itself after five minutes.");
            ChkPolicy.Content = T("AiSetupPolicyCheck", "I understand where my questions and recordings go");

            TxtProviderTitle.Text = T("AiSetupProviderTitle", "Choose a provider");
            TxtProviderSub.Text = T("AiSetupProviderSub",
                "Each one is a separate account with its own key. Not sure? Start with Gemini — it is free and hears your voice directly. " +
                "You can add the others later and switch at any time.");

            TxtPasteLabel.Text = T("AiSetupPasteLabel", "Paste your key here");
            BtnPasteKey.Content = T("AiSetupPaste", "Paste");

            TxtTestTitle.Text = T("AiSetupTestTitle", "Check that it works");
            TxtTestSub.Text = T("AiSetupTestSub",
                "Sends one tiny text question with your key and shows exactly what came back. It costs next to nothing.");
            TxtModelLabel.Text = T("AiSetupModelLabel", "Model");
            BtnBrowseModels.Content = T("AiSetupBrowseModels", "Browse");
            BtnRunTest.Content = T("AiSetupRunTest", "Test again");

            TxtFinishTitle.Text = T("AiSetupFinishTitle", "You're ready");
            TxtFinishSub.Text = T("AiSetupFinishSub", "Three last choices about how you ask and how you get answers. All of them can be changed later.");
            TxtHotkeyLabel.Text = T("AiSetupHotkeyLabel", "Push-to-talk hotkey");
            BtnHotkey.Content = T("AiCompanionSetHotkey", "Set hotkey");
            TxtTextAnswersLabel.Text = T("AiSetupTextAnswers", "Show answers on screen");
            TxtTextAnswersHint.Text = T("AiSetupTextAnswersHint", "A small panel over the game shows the answer as it is written.");
            TxtVoiceAnswersLabel.Text = T("AiSetupVoiceAnswers", "Read answers out loud");
            TxtHowToTitle.Text = T("AiSetupHowToTitle", "How to ask a question");
            TxtFinishNote.Text = T("AiSetupFinishNote",
                "Everything else — the voice model, game data, answer window size and colour, custom rules — is under Settings › AI Companion. " +
                "Recent questions and any errors are listed there too.");
        }

        private void BuildWelcome()
        {
            WelcomeFeatures.Children.Clear();
            WelcomeFeatures.Children.Add(Feature(WpfUi.SymbolRegular.Mic24,
                T("AiSetupFeatureVoice", "Ask out loud"),
                T("AiSetupFeatureVoiceBody", "Hold a hotkey, ask \"how many rockets for a stone wall?\", let go.")));
            WelcomeFeatures.Children.Add(Feature(WpfUi.SymbolRegular.Camera24,
                T("AiSetupFeatureShot", "Show it your screen"),
                T("AiSetupFeatureShotBody", "Attach a screenshot and ask what something is or what to do next.")));
            WelcomeFeatures.Children.Add(Feature(WpfUi.SymbolRegular.Speaker224,
                T("AiSetupFeatureAnswer", "Read or hear the answer"),
                T("AiSetupFeatureAnswerBody", "Answers appear over the game, read aloud if you like, using current recipes and raid costs.")));
        }

        private static UIElement Feature(WpfUi.SymbolRegular icon, string title, string body)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tile = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0x60, 0xCD, 0xFF)),
                Margin = new Thickness(0, 0, 12, 0),
                Child = new WpfUi.SymbolIcon
                {
                    Symbol = icon,
                    FontSize = 18,
                    Foreground = Brush("Accent"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            grid.Children.Add(tile);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextPrimary") });
            text.Children.Add(new TextBlock
            {
                Text = body,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextSubtle"),
                Margin = new Thickness(0, 2, 0, 0),
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            return grid;
        }

        private void OnPolicyToggled(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            UpdateFooter();
        }

        // ------------------------------------------------------------------ 2. provider

        /// <summary>One selectable card per provider, with what it costs and what it can do.</summary>
        private void BuildProviderCards()
        {
            ProviderCards.Children.Clear();

            foreach (var provider in RecommendedOrder)
            {
                var card = new Border
                {
                    Tag = provider,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1.5),
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Focusable = true,
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var radio = new RadioButton
                {
                    GroupName = "AiSetupProvider",
                    VerticalAlignment = VerticalAlignment.Top,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 1, 8, 0),
                    IsHitTestVisible = false,
                    Focusable = false,
                };
                grid.Children.Add(radio);

                var body = new StackPanel();
                Grid.SetColumn(body, 1);

                var head = new WrapPanel();
                head.Children.Add(new TextBlock
                {
                    Text = AiProviders.DisplayName(provider),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("TextPrimary"),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });

                if (provider == AiProviders.Gemini)
                    head.Children.Add(Pill(T("AiSetupRecommended", "Recommended to start"), Color.FromRgb(0x60, 0xCD, 0xFF)));

                if (AiCompanionStore.HasKeyFor(provider))
                    head.Children.Add(Pill(T("AiSetupKeyStored", "Key stored"), Color.FromRgb(0x4C, 0xAF, 0x50)));

                body.Children.Add(head);

                body.Children.Add(new TextBlock
                {
                    Text = Tagline(provider),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("TextSubtle"),
                    Margin = new Thickness(0, 4, 0, 8),
                });

                var facts = new WrapPanel();
                facts.Children.Add(Fact(AiProviders.HasFreeTier(provider),
                    provider == AiProviders.OpenRouter
                        ? T("AiSetupFactFreeModels", "Free models available")
                        : AiProviders.HasFreeTier(provider)
                            ? T("AiSetupFactFree", "Free tier, no card")
                            : T("AiSetupFactPaid", "Needs prepaid credit ($5+)")));
                facts.Children.Add(Fact(AiProviders.AcceptsAudio(provider),
                    AiProviders.AcceptsAudio(provider)
                        ? T("AiSetupFactHears", "Hears your voice directly")
                        : T("AiSetupFactTranscribed", "Speech turned into text by Windows first")));
                facts.Children.Add(Fact(AiProviders.HasVoice(provider),
                    AiProviders.HasVoice(provider)
                        ? T("AiSetupFactVoice", "Natural spoken answers")
                        : T("AiSetupFactWindowsVoice", "Windows reads answers aloud")));
                body.Children.Add(facts);

                grid.Children.Add(body);
                card.Child = grid;

                card.MouseLeftButtonUp += (_, _) => SelectProvider(provider);
                card.KeyDown += (_, e) =>
                {
                    if (e.Key is System.Windows.Input.Key.Space or System.Windows.Input.Key.Enter)
                    {
                        SelectProvider(provider);
                        e.Handled = true;
                    }
                };

                ProviderCards.Children.Add(card);
            }

            RefreshProviderCards();
        }

        /// <summary>The free ones first: the order is advice, not the alphabet.</summary>
        private static readonly string[] RecommendedOrder =
        {
            AiProviders.Gemini, AiProviders.OpenRouter, AiProviders.OpenAi, AiProviders.Anthropic,
        };

        private void SelectProvider(string provider)
        {
            if (_provider != provider)
            {
                _provider = provider;
                _testPassed = false;

                // A key pasted for the last provider is not one for this one.
                PwdKey.Password = "";
            }

            RefreshProviderCards();
        }

        private void RefreshProviderCards()
        {
            foreach (var child in ProviderCards.Children)
            {
                if (child is not Border card || card.Tag is not string provider) continue;

                bool selected = provider == _provider;
                card.BorderBrush = selected ? Brush("Accent") : Brush("CardBorder");
                card.Background = selected
                    ? new SolidColorBrush(Color.FromArgb(0x18, 0x60, 0xCD, 0xFF))
                    : Brush("SurfaceAlt");

                if (card.Child is Grid grid && grid.Children[0] is RadioButton radio)
                    radio.IsChecked = selected;
            }
        }

        private void SaveProvider()
        {
            var settings = AiCompanionStore.Current;
            if (settings.Provider == _provider) return;

            settings.Provider = _provider;
            AiCompanionStore.Save(settings);
        }

        private static string Tagline(string provider) => provider switch
        {
            AiProviders.Gemini => T("AiSetupTaglineGemini",
                "Google's models. A free allowance with any Google account — no card needed. Fast, listens to the recording itself and can speak answers."),
            AiProviders.OpenRouter => T("AiSetupTaglineOpenRouter",
                "One key for hundreds of models from many companies, including free ones. Good if you want to try different models."),
            AiProviders.OpenAi => T("AiSetupTaglineOpenAi",
                "The models behind ChatGPT. Very good at understanding speech and has the most natural voice. Paid per use — ChatGPT Plus does not cover it."),
            AiProviders.Anthropic => T("AiSetupTaglineAnthropic",
                "Claude models. Careful, well-written answers. Paid per use and cannot hear audio, so your question is transcribed on this PC first."),
            _ => "",
        };

        private static UIElement Fact(bool good, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2) };
            panel.Children.Add(new WpfUi.SymbolIcon
            {
                Symbol = good ? WpfUi.SymbolRegular.CheckmarkCircle16 : WpfUi.SymbolRegular.Info16,
                FontSize = 13,
                Foreground = good ? Brush("SuccessBrush") : Brush("TextSubtle"),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = good ? Brush("TextPrimary") : Brush("TextSubtle"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return panel;
        }

        private static Border Pill(string text, Color color) => new()
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color),
            },
        };

        // ------------------------------------------------------------------ 3. key

        private void BuildKeyStep()
        {
            var name = AiProviders.DisplayName(_provider);

            TxtKeyTitle.Text = string.Format(T("AiSetupKeyTitle", "Get your {0} key"), name);
            TxtKeySub.Text = T("AiSetupKeySub",
                "An API key is a long password that lets this app use your account. Follow these steps in your browser, then paste the key below.");

            GuideSteps.Children.Clear();
            var steps = GuideFor(_provider);
            for (int i = 0; i < steps.Length; i++)
                GuideSteps.Children.Add(GuideStep(i + 1, steps[i]));

            BtnOpenKeyPage.Content = string.Format(T("AiSetupOpenKeyPage", "Open {0}"), SiteName(_provider));

            var billing = AiProviders.BillingUrl(_provider);
            BtnOpenBilling.Visibility = string.IsNullOrEmpty(billing) ? Visibility.Collapsed : Visibility.Visible;
            BtnOpenBilling.Content = _provider == AiProviders.OpenRouter
                ? T("AiSetupAddCreditOptional", "Add credit (optional)")
                : T("AiSetupAddCredit", "Add credit");

            TxtCostNote.Text = CostNote(_provider);

            if (AiCompanionStore.HasKeyFor(_provider) && PwdKey.Password.Length == 0)
                PwdKey.PlaceholderText = T("AiSetupKeyKeepPlaceholder", "A key is already stored — leave empty to keep it");
            else
                PwdKey.PlaceholderText = string.Format(T("AiSetupKeyPlaceholder", "Starts with {0}…"), AiProviders.KeyPrefix(_provider));

            UpdateKeyCheck();
        }

        /// <summary>
        /// The provider's own steps, in the words of the buttons on its pages.
        ///
        /// These pages change. The wording is kept to the parts that have stayed put for
        /// years — the page names and the one button — rather than describing layouts.
        /// </summary>
        private static string[] GuideFor(string provider) => provider switch
        {
            AiProviders.Gemini => new[]
            {
                T("AiSetupGeminiStep1", "Click \"Open Google AI Studio\" below and sign in with any Google account."),
                T("AiSetupGeminiStep2", "Accept the terms if asked, then click \"Create API key\". If it asks for a project, pick the suggested one or create a new one — either is fine."),
                T("AiSetupGeminiStep3", "Copy the key it shows (it starts with AIza) and paste it below. No payment details are needed."),
            },
            AiProviders.OpenRouter => new[]
            {
                T("AiSetupOpenRouterStep1", "Click \"Open OpenRouter\" below and sign in (Google, GitHub or email all work)."),
                T("AiSetupOpenRouterStep2", "On the Keys page click \"Create API Key\". Give it a name like \"Rust+ Desktop\"; you can leave the credit limit empty."),
                T("AiSetupOpenRouterStep3", "Copy the key (it starts with sk-or-v1-) and paste it below. It is shown only once — create a new one if you lose it."),
                T("AiSetupOpenRouterStep4", "Optional: add a few dollars of credit to use paid models and raise the daily limit on free ones."),
            },
            AiProviders.OpenAi => new[]
            {
                T("AiSetupOpenAiStep1", "Click \"Add credit\" below, sign in to the OpenAI Platform (your ChatGPT login works) and add at least $5. The API is billed separately from ChatGPT Plus."),
                T("AiSetupOpenAiStep2", "Click \"Open OpenAI Platform\" to go to the API keys page and press \"Create new secret key\". Name it \"Rust+ Desktop\" and leave permissions on \"All\"."),
                T("AiSetupOpenAiStep3", "Copy the key (it starts with sk-) and paste it below. It is shown only once."),
            },
            AiProviders.Anthropic => new[]
            {
                T("AiSetupAnthropicStep1", "Click \"Add credit\" below and create a Claude Console account — it is separate from a claude.ai chat subscription. Buy at least $5 of credit."),
                T("AiSetupAnthropicStep2", "Click \"Open Claude Console\" to go to API keys and press \"Create Key\". Name it \"Rust+ Desktop\"."),
                T("AiSetupAnthropicStep3", "Copy the key (it starts with sk-ant-) and paste it below. It is shown only once."),
            },
            _ => Array.Empty<string>(),
        };

        private static string SiteName(string provider) => provider switch
        {
            AiProviders.Gemini => "Google AI Studio",
            AiProviders.OpenRouter => "OpenRouter",
            AiProviders.OpenAi => "OpenAI Platform",
            AiProviders.Anthropic => "Claude Console",
            _ => provider,
        };

        private static string CostNote(string provider) => provider switch
        {
            AiProviders.Gemini => T("AiSetupCostGemini",
                "Free: Google gives the API a daily allowance at no cost. Busy sessions can hit a per-minute limit — wait a moment and ask again. " +
                "On the free tier Google may use what you send to improve its products."),
            AiProviders.OpenRouter => T("AiSetupCostOpenRouter",
                "Free models (marked Free) cost nothing but have a small daily limit and may log what you send. " +
                "Paid models use pay-as-you-go credit; cheap ones cost a fraction of a cent per question."),
            AiProviders.OpenAi => T("AiSetupCostOpenAi",
                "Pay as you go from prepaid credit. A typical question costs about a cent; $5 lasts most players a long time. " +
                "Without credit every request is refused, even with a valid key."),
            AiProviders.Anthropic => T("AiSetupCostAnthropic",
                "Pay as you go from prepaid credit. A typical question costs one or two cents. " +
                "Without credit every request is refused, even with a valid key."),
            _ => "",
        };

        private static UIElement GuideStep(int number, string text)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = Brush("Accent"),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = number.ToString(),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("AppBg"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });

            var label = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
                Foreground = Brush("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);

            return grid;
        }

        private void BtnOpenKeyPage_Click(object sender, RoutedEventArgs e) => OpenUrl(AiProviders.KeyUrl(_provider));

        private void BtnOpenBilling_Click(object sender, RoutedEventArgs e) => OpenUrl(AiProviders.BillingUrl(_provider));

        private void BtnPasteKey_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Clipboard.ContainsText()) PwdKey.Password = Clipboard.GetText().Trim();
            }
            catch
            {
                // The clipboard can be held by another process for a moment; Ctrl+V still works.
            }
        }

        private void PwdKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // A new key has not been tested, whatever the old one did.
            _testPassed = false;
            UpdateKeyCheck();
            UpdateFooter();
        }

        private string TypedKey => (PwdKey.Password ?? "").Trim().Trim('"', '\'');

        private bool KeyStepComplete()
        {
            var key = TypedKey;
            if (key.Length == 0) return AiCompanionStore.HasKeyFor(_provider);

            var owner = WhoseKey(key);
            return (owner == null || owner == _provider) && AiProviders.LooksLikeKey(_provider, key);
        }

        /// <summary>Says while typing whether the paste looks like this provider's key.</summary>
        private void UpdateKeyCheck()
        {
            var key = TypedKey;
            var prefix = AiProviders.KeyPrefix(_provider);

            if (key.Length == 0)
            {
                SetKeyCheck(WpfUi.SymbolRegular.Info16, "TextSubtle", AiCompanionStore.HasKeyFor(_provider)
                    ? T("AiSetupKeyCheckKeep", "A key for this provider is already stored. Paste a new one to replace it, or press Next to keep it.")
                    : T("AiSetupKeyCheckEmpty", "Stored only on this PC, encrypted with your Windows account."));
                return;
            }

            // A key from the wrong provider is the most common mistake, and the prefix says
            // exactly whose it is. Checked first: an Anthropic key starts with sk- too, so it
            // would otherwise pass for an OpenAI one.
            var other = WhoseKey(key);
            if (other != null && other != _provider)
            {
                SetKeyCheck(WpfUi.SymbolRegular.Warning16, "DangerBrush", string.Format(
                    T("AiSetupKeyCheckWrongProvider", "This looks like a {0} key, not a {1} key. Go back and pick {0}, or get a {1} key."),
                    AiProviders.DisplayName(other), AiProviders.DisplayName(_provider)));
                return;
            }

            if (AiProviders.LooksLikeKey(_provider, key) &&
                (prefix.Length == 0 || key.StartsWith(prefix, StringComparison.Ordinal)))
            {
                SetKeyCheck(WpfUi.SymbolRegular.CheckmarkCircle16, "SuccessBrush",
                    T("AiSetupKeyCheckOk", "Looks like a valid key. Press Next to save and test it."));
                return;
            }

            if (AiProviders.LooksLikeKey(_provider, key))
            {
                SetKeyCheck(WpfUi.SymbolRegular.Warning16, "TextSubtle", string.Format(
                    T("AiSetupKeyCheckUnusual", "{0} keys usually start with {1}. It may still work — the test will tell."),
                    AiProviders.DisplayName(_provider), prefix));
                return;
            }

            SetKeyCheck(WpfUi.SymbolRegular.Warning16, "DangerBrush", string.Format(
                T("AiSetupKeyCheckBad", "That is too short to be a key. Copy the whole thing — it starts with {0}."), prefix));
        }

        private static string? WhoseKey(string key)
        {
            if (key.StartsWith("sk-ant-", StringComparison.Ordinal)) return AiProviders.Anthropic;
            if (key.StartsWith("sk-or-", StringComparison.Ordinal)) return AiProviders.OpenRouter;
            if (key.StartsWith("AIza", StringComparison.Ordinal)) return AiProviders.Gemini;
            if (key.StartsWith("sk-", StringComparison.Ordinal)) return AiProviders.OpenAi;
            return null;
        }

        private void SetKeyCheck(WpfUi.SymbolRegular icon, string brush, string text)
        {
            KeyCheckIcon.Symbol = icon;
            KeyCheckIcon.Foreground = Brush(brush);
            TxtKeyCheck.Foreground = brush == "TextSubtle" ? Brush("TextSubtle") : Brush(brush);
            TxtKeyCheck.Text = text;
        }

        private bool SaveKey()
        {
            var key = TypedKey;

            if (!KeyStepComplete()) return false;
            if (key.Length == 0) return true;

            AiCompanionStore.WriteKeyFor(_provider, key);
            PwdKey.Password = "";
            _testPassed = false;
            return true;
        }

        // ------------------------------------------------------------------ 4. test

        private void BuildTestStep()
        {
            BtnBrowseModels.Visibility = _provider == AiProviders.OpenRouter ? Visibility.Visible : Visibility.Collapsed;

            var settings = AiCompanionStore.Current;
            var current = settings.Models.TryGetValue(_provider, out var chosen) ? chosen?.Trim() ?? "" : "";

            // OpenRouter's default is a paid model, and a key made a minute ago has no credit
            // behind it. Starting on the free router means the first test can pass; anyone
            // with credit picks a paid model from the same list.
            if (_provider == AiProviders.OpenRouter && current.Length == 0)
            {
                current = OpenRouterFree;
                settings.Models[_provider] = current;
                AiCompanionStore.Save(settings);
            }

            _loading = true;
            try
            {
                CmbModel.Items.Clear();

                foreach (var (id, label) in ModelChoices(_provider))
                    CmbModel.Items.Add(new ComboBoxItem { Content = label, Tag = id });

                int match = -1;
                for (int i = 0; i < CmbModel.Items.Count; i++)
                {
                    if (string.Equals((CmbModel.Items[i] as ComboBoxItem)?.Tag as string, current, StringComparison.OrdinalIgnoreCase))
                    {
                        match = i;
                        break;
                    }
                }

                if (match < 0)
                {
                    // Something typed in the settings page or picked from the browser: shown
                    // as itself rather than replaced by a default.
                    CmbModel.Items.Add(new ComboBoxItem { Content = current, Tag = current });
                    match = CmbModel.Items.Count - 1;
                }

                CmbModel.SelectedIndex = match;
            }
            finally
            {
                _loading = false;
            }

            UpdateModelNote();
        }

        /// <summary>
        /// A short list a beginner can choose from without knowing model names. The first row
        /// is always the app's default, stored as no override.
        /// </summary>
        /// <summary>OpenRouter's router over its free models: the one choice that needs no credit.</summary>
        private const string OpenRouterFree = "openrouter/free";

        private static (string Id, string Label)[] ModelChoices(string provider)
        {
            var list = new System.Collections.Generic.List<(string, string)>
            {
                ("", string.Format(T("AiSetupModelDefault", "Recommended — {0}"), AiProviders.DefaultModel(provider))),
            };

            if (provider == AiProviders.OpenRouter)
            {
                list.Insert(0, (OpenRouterFree, T("AiSetupModelOpenRouterFree", "Free — openrouter/free (works without credit)")));
            }

            foreach (var id in AiProviders.SuggestedModels(provider))
            {
                if (id == AiProviders.DefaultModel(provider)) continue;
                list.Add((id, id));
            }

            return list.ToArray();
        }

        private void CmbModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (CmbModel.SelectedItem is not ComboBoxItem item) return;

            SaveModel(item.Tag as string ?? "");
        }

        private void SaveModel(string id)
        {
            var settings = AiCompanionStore.Current;
            var before = settings.Models.TryGetValue(_provider, out var old) ? old ?? "" : "";

            if (id.Length == 0) settings.Models.Remove(_provider);
            else settings.Models[_provider] = id;
            AiCompanionStore.Save(settings);

            if (!string.Equals(before, id, StringComparison.OrdinalIgnoreCase))
            {
                _testPassed = false;
                TestResult.Visibility = Visibility.Collapsed;
                UpdateFooter();
            }

            UpdateModelNote();
        }

        private void UpdateModelNote()
        {
            TxtModelNote.Text = _provider == AiProviders.OpenRouter
                ? T("AiSetupModelNoteOpenRouter",
                    "Browse to see every model with its price. Models marked Free cost nothing; pick one that supports vision if you want to send screenshots.")
                : T("AiSetupModelNote", "The recommended model is a good balance of speed, quality and cost. You can change it later in the settings.");
        }

        private void BtnBrowseModels_Click(object sender, RoutedEventArgs e)
        {
            var current = AiProviders.Model(_provider);
            var picker = new OpenRouterModelPickerWindow(current) { Owner = this };

            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedModelId))
            {
                SaveModel(picker.SelectedModelId.Trim());
                BuildTestStep();
                _ = RunTestAsync();
            }
        }

        private void BtnRunTest_Click(object sender, RoutedEventArgs e) => _ = RunTestAsync();

        /// <summary>
        /// Asks the provider one fixed, tiny question with the stored key and the chosen model,
        /// outside the companion's own state so nothing shows up in the game overlay.
        /// </summary>
        private async Task RunTestAsync()
        {
            if (_testing) return;

            var key = AiCompanionStore.ReadKeyFor(_provider);
            var model = AiProviders.Model(_provider);

            int run = ++_testRun;
            _testing = true;
            _testPassed = false;
            BtnRunTest.IsEnabled = false;
            ShowTestPending(model);
            UpdateFooter();

            _testCancel?.Cancel();
            _testCancel = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var ct = _testCancel.Token;

            string? answer = null;
            string? error = null;

            try
            {
                if (string.IsNullOrEmpty(key))
                {
                    error = T("AiSetupTestNoKey", "No key is stored for this provider yet.");
                }
                else
                {
                    var question = new AiQuestion
                    {
                        Text = "Connection check. Reply with exactly: Ready to raid.",
                        Instructions = "You are answering a connection test from a desktop app. Reply with exactly the words you are asked for and nothing else.",
                        MaxWords = 8,
                        Language = "English",
                        MatchQuestionLanguage = false,
                    };

                    var provider = AiProviderFactory.For(_provider);
                    var result = await Task.Run(() => provider.AskAsync(question, key, null, ct), ct);
                    answer = AiPrompt.CleanResponse(result.Answer);

                    if (string.IsNullOrWhiteSpace(answer))
                        error = T("AiSetupTestEmpty", "The provider answered, but with nothing in it. Try another model.");
                }
            }
            catch (OperationCanceledException)
            {
                error = T("AiSetupTestTimeout", "The provider took too long to answer.");
            }
            catch (AiRequestException ex)
            {
                error = ex.Message;
            }
            catch (System.Net.Http.HttpRequestException)
            {
                error = T("AiSetupTestOffline", "Could not reach the provider. Check your internet connection and try again.");
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            // Abandoned — Back, a step on the rail, the window closing, or a newer run. Whoever
            // abandoned it has already reset the state; a late result must not overwrite it.
            if (run != _testRun || !IsLoaded) return;

            _testing = false;
            BtnRunTest.IsEnabled = true;

            AiLog.Info(error == null
                ? $"Setup test against {_provider} ({model}) succeeded."
                : $"Setup test against {_provider} ({model}) failed: {error}");

            if (error == null)
            {
                _testPassed = true;
                ShowTestSuccess(model, answer!);
            }
            else
            {
                ShowTestFailure(error);
            }

            UpdateFooter();
        }

        private void CancelTest()
        {
            if (!_testing) return;

            _testRun++;
            _testCancel?.Cancel();
            _testing = false;
            BtnRunTest.IsEnabled = true;
            TestResult.Visibility = Visibility.Collapsed;
        }

        private void ShowTestPending(string model)
        {
            TestResult.Visibility = Visibility.Visible;
            TestSpinner.Visibility = Visibility.Visible;
            TestIcon.Visibility = Visibility.Collapsed;
            TestResult.BorderBrush = Brush("CardBorder");
            TxtTestResultTitle.Text = string.Format(T("AiSetupTestPending", "Asking {0}…"), model);
            TxtTestResultBody.Text = T("AiSetupTestPendingBody", "Usually takes a couple of seconds. Free models can take longer.");
            TestFixes.Children.Clear();
            TestActions.Children.Clear();
        }

        private void ShowTestSuccess(string model, string answer)
        {
            TestResult.Visibility = Visibility.Visible;
            TestSpinner.Visibility = Visibility.Collapsed;
            TestIcon.Visibility = Visibility.Visible;
            TestIcon.Symbol = WpfUi.SymbolRegular.CheckmarkCircle24;
            TestIcon.Foreground = Brush("SuccessBrush");
            TestResult.BorderBrush = Brush("SuccessBrush");

            TxtTestResultTitle.Text = T("AiSetupTestOk", "It works!");
            TxtTestResultBody.Text = string.Format(
                T("AiSetupTestOkBody", "{0} answered: \"{1}\""), model, Shorten(answer, 160));
            TestFixes.Children.Clear();
            TestActions.Children.Clear();
        }

        /// <summary>
        /// The provider's own reason, then what to do about it. The reasons are the sentences
        /// <see cref="AiHttp"/> writes, so they are matched on how those begin.
        /// </summary>
        private void ShowTestFailure(string error)
        {
            TestResult.Visibility = Visibility.Visible;
            TestSpinner.Visibility = Visibility.Collapsed;
            TestIcon.Visibility = Visibility.Visible;
            TestIcon.Symbol = WpfUi.SymbolRegular.ErrorCircle24;
            TestIcon.Foreground = Brush("DangerBrush");
            TestResult.BorderBrush = Brush("DangerBrush");

            TxtTestResultTitle.Text = T("AiSetupTestFailed", "That didn't work yet");
            TxtTestResultBody.Text = error;

            TestFixes.Children.Clear();
            TestActions.Children.Clear();

            var lower = error.ToLowerInvariant();
            bool keyProblem = lower.Contains("rejected the key") || lower.Contains("no key") || lower.Contains("api key not valid") || lower.Contains("invalid");
            bool creditProblem = lower.Contains("credit") || lower.Contains("billed") || lower.Contains("quota") || lower.Contains("balance");
            bool modelProblem = lower.Contains("model");
            bool rateProblem = lower.Contains("too many requests");

            if (keyProblem)
            {
                AddFix(T("AiSetupFixKey1", "Copy the key again — all of it, without spaces or quotes."));
                AddFix(T("AiSetupFixKey2", "Make sure it is a key for the provider you picked. Keys are shown only once; if in doubt, create a new one."));
                AddAction(T("AiSetupFixKeyAction", "Paste a different key"), () => ShowStep(Step.Key));
            }

            if (creditProblem)
            {
                AddFix(_provider == AiProviders.Gemini
                    ? T("AiSetupFixCreditGemini", "The free allowance may be used up for today, or not offered in your region. Wait and retry, or enable billing on the Google project.")
                    : T("AiSetupFixCredit", "The account has no credit. Add some on the provider's billing page — it can take a minute or two to apply."));

                var billing = AiProviders.BillingUrl(_provider);
                if (!string.IsNullOrEmpty(billing))
                    AddAction(T("AiSetupAddCredit", "Add credit"), () => OpenUrl(billing));
            }

            if (modelProblem && !keyProblem && !creditProblem)
                AddFix(T("AiSetupFixModel", "Pick a different model above and test again."));

            if (rateProblem && !creditProblem)
                AddFix(T("AiSetupFixRate", "Free models get busy. Wait a few seconds and test again, or pick another model."));

            if (TestFixes.Children.Count == 0)
                AddFix(T("AiSetupFixGeneric", "Try again in a moment. If it keeps failing, pick another model or check the key."));

            AddAction(T("AiSetupRunTest", "Test again"), () => _ = RunTestAsync());
        }

        private void AddFix(string text)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = "•", Margin = new Thickness(0, 0, 6, 0), Foreground = Brush("TextSubtle") });
            var label = new TextBlock { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush("TextPrimary") };
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            TestFixes.Children.Add(row);
        }

        private void AddAction(string text, Action onClick)
        {
            var button = new WpfUi.Button
            {
                Content = text,
                Appearance = WpfUi.ControlAppearance.Secondary,
                Height = 30,
                Padding = new Thickness(12, 0, 12, 0),
                Margin = new Thickness(0, 0, 8, 0),
            };
            button.Click += (_, _) => onClick();
            TestActions.Children.Add(button);
        }

        // ------------------------------------------------------------------ 5. finish

        private void BuildFinishStep()
        {
            var settings = AiCompanionStore.Current;

            TxtHotkey.Text = string.IsNullOrWhiteSpace(settings.Hotkey)
                ? T("AiSetupHotkeyNone", "Not set. Pick a key you can hold while playing, such as F8 or Alt+Q.")
                : string.Format(T("AiSetupHotkeySet", "Hold {0}, ask, let go."), settings.Hotkey);

            TxtVoiceAnswersHint.Text = AiProviders.HasVoice(_provider)
                ? T("AiSetupVoiceHintNative", "Spoken in the provider's own natural voice.")
                : T("AiSetupVoiceHintWindows", "Read by the Windows voice — clear, but plainly a synthesiser.");

            HowToSteps.Children.Clear();
            HowToSteps.Children.Add(GuideStep(1, string.IsNullOrWhiteSpace(settings.Hotkey)
                ? T("AiSetupHowTo1Tile", "Add the AI tile to your Command Dock, click it and ask your question.")
                : string.Format(T("AiSetupHowTo1Hotkey", "In game, hold {0} and ask — or click the AI tile in your Command Dock."), settings.Hotkey)));
            HowToSteps.Children.Add(GuideStep(2, settings.AutoSendAfterRecording
                ? T("AiSetupHowTo2Auto", "Let go, and the question is sent straight away.")
                : T("AiSetupHowTo2Send", "Let go, then press Send on the tile. (Auto-send can be turned on in the settings.)")));
            HowToSteps.Children.Add(GuideStep(3, T("AiSetupHowTo3",
                "Use the camera button on the tile to attach a screenshot when you want it to see what you see.")));

            // The chat command answers through the same key, so it belongs here too — with its
            // two conditions said up front rather than found out from a refusal in team chat.
            HowToSteps.Children.Add(GuideStep(4, RustPlusDesk.Services.Auth.SupabaseAuthManager.IsPremium
                ? T("AiSetupHowTo4Chat",
                    "Or type !ai followed by your question in team chat, e.g. \"!ai how much sulfur for a garage door?\". The short answer is posted back to chat. Teammates can use it too once you allow them in the chat command settings.")
                : T("AiSetupHowTo4ChatSupporter",
                    "Supporters can also type !ai followed by a question in team chat and get the answer posted back in game.")));
        }

        private void BtnHotkey_Click(object sender, RoutedEventArgs e)
        {
            var capture = new HotkeyCaptureWindow { Owner = this, AllowClear = true };
            if (capture.ShowDialog() != true) return;

            var settings = AiCompanionStore.Current;
            settings.Hotkey = capture.Gesture?.Trim() ?? "";
            AiCompanionStore.Save(settings);

            (Application.Current?.MainWindow as RustPlusDesk.Views.MainWindow)?.ApplyAiHotkey();

            BuildFinishStep();
        }

        private void OnAnswerModeChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            // One of the two has to stay on, or an answer arrives with nowhere to go.
            if (ChkTextAnswers.IsChecked != true && ChkVoiceAnswers.IsChecked != true)
            {
                _loading = true;
                if (ReferenceEquals(sender, ChkTextAnswers)) ChkVoiceAnswers.IsChecked = true;
                else ChkTextAnswers.IsChecked = true;
                _loading = false;
            }

            var settings = AiCompanionStore.Current;
            settings.TextAnswers = ChkTextAnswers.IsChecked == true;
            settings.AudioAnswers = ChkVoiceAnswers.IsChecked == true;
            AiCompanionStore.Save(settings);
        }

        // ------------------------------------------------------------------ helpers

        private static string T(string key, string fallback) => Loc.Text(key, fallback);

        private static Brush Brush(string key) =>
            Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

        private static string Shorten(string text, int max)
        {
            text = text.Trim().Replace('\n', ' ');
            return text.Length <= max ? text : text[..max] + "…";
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AiLog.Info($"Could not open {url}: {ex.Message}");
            }
        }
    }
}
