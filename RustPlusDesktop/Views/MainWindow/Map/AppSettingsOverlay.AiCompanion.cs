using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Helpers;
using RustPlusDesk.Services.AiCompanion;
using RustPlusDesk.Services.Auth;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    /// <summary>
    /// The AI companion's settings.
    ///
    /// It differs from the other connected services in one way worth being loud about: the
    /// connection is between this machine and the user's own account with a model provider.
    /// Nothing here goes through our cloud, and the key is never uploaded. The panel says so,
    /// the policy dialog says so before a key can be stored, and the store that keeps it has no
    /// network call in it.
    /// </summary>
    public partial class AppSettingsOverlay : UserControl
    {
        private void LoadAiCompanionSettings()
        {
            var settings = AiCompanionStore.Current;

            if (CmbAiProvider.Items.Count == 0)
            {
                foreach (var provider in AiProviders.All)
                    CmbAiProvider.Items.Add(new ComboBoxItem
                    {
                        Content = AiProviders.DisplayName(provider),
                        Tag = provider,
                    });
            }

            CmbAiProvider.SelectedIndex = Math.Max(0, Array.IndexOf(AiProviders.All, settings.Provider));

            ChkAiTextAnswers.IsChecked = settings.TextAnswers;
            ChkAiAudioAnswers.IsChecked = settings.AudioAnswers;
            ChkAiStreamingVoice.IsChecked = settings.StreamingVoice;
            ChkAiGameAudio.IsChecked = settings.CaptureGameAudio;
            ChkAiScreenshotDefault.IsChecked = settings.AttachScreenshotByDefault;
            ChkAiAutoSend.IsChecked = settings.AutoSendAfterRecording;
            ChkAiGameData.IsChecked = settings.IncludeGameData;

            if (CmbAiAnswerLanguage.Items.Count == 0)
            {
                foreach (var (key, label) in AnswerLanguageChoices)
                    CmbAiAnswerLanguage.Items.Add(new ComboBoxItem { Content = label(), Tag = key });
            }

            CmbAiAnswerLanguage.SelectedIndex = Math.Max(0,
                Array.FindIndex(AnswerLanguageChoices, entry => entry.Key == settings.AnswerLanguage));

            if (CmbAiShotZoom.Items.Count == 0)
            {
                foreach (var (zoom, label) in ShotZooms)
                    CmbAiShotZoom.Items.Add(new ComboBoxItem { Content = label(), Tag = zoom });
            }

            CmbAiShotZoom.SelectedIndex = Math.Max(0,
                Array.FindIndex(ShotZooms, entry => Math.Abs(entry.Zoom - settings.ScreenshotZoom) < 0.01));

            if (CmbAiAnswerColor.Items.Count == 0)
            {
                foreach (var key in Models.CommandDockTextColors.All)
                    CmbAiAnswerColor.Items.Add(new ComboBoxItem
                    {
                        Content = AnswerColorLabel(key),
                        Tag = key,
                    });
            }

            CmbAiAnswerColor.SelectedIndex = Math.Max(0,
                Array.IndexOf(Models.CommandDockTextColors.All,
                    settings.AnswerTextColorKey ?? Models.CommandDockTextColors.Auto));

            SliAiAnswerOpacity.Value = Math.Clamp(settings.AnswerOpacity, 0.0, 1.0);
            SliAiAnswerWidth.Value = Math.Clamp(settings.AnswerWidth, 220, 900);
            SliAiAnswerHeight.Value = Math.Clamp(settings.AnswerHeight, 80, 800);

            if (CmbOpenRouterReasoning.Items.Count == 0)
            {
                CmbOpenRouterReasoning.Items.Add(new ComboBoxItem { Content = "Auto (Model Default)", Tag = "auto" });
                CmbOpenRouterReasoning.Items.Add(new ComboBoxItem { Content = "Low Effort", Tag = "low" });
                CmbOpenRouterReasoning.Items.Add(new ComboBoxItem { Content = "Medium Effort", Tag = "medium" });
                CmbOpenRouterReasoning.Items.Add(new ComboBoxItem { Content = "High Effort", Tag = "high" });
                CmbOpenRouterReasoning.Items.Add(new ComboBoxItem { Content = "None / Disabled", Tag = "none" });
            }

            var currentEffort = settings.OpenRouter.ReasoningEffort ?? "auto";
            int effortIdx = 0;
            for (int i = 0; i < CmbOpenRouterReasoning.Items.Count; i++)
            {
                if (CmbOpenRouterReasoning.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, currentEffort, StringComparison.OrdinalIgnoreCase))
                {
                    effortIdx = i;
                    break;
                }
            }
            CmbOpenRouterReasoning.SelectedIndex = effortIdx;

            if (CmbOpenRouterThinkingTokens.Items.Count == 0)
            {
                CmbOpenRouterThinkingTokens.Items.Add(new ComboBoxItem { Content = "Auto (Default)", Tag = 0 });
                CmbOpenRouterThinkingTokens.Items.Add(new ComboBoxItem { Content = "1,024 tokens", Tag = 1024 });
                CmbOpenRouterThinkingTokens.Items.Add(new ComboBoxItem { Content = "2,048 tokens", Tag = 2048 });
                CmbOpenRouterThinkingTokens.Items.Add(new ComboBoxItem { Content = "4,096 tokens", Tag = 4096 });
                CmbOpenRouterThinkingTokens.Items.Add(new ComboBoxItem { Content = "8,192 tokens", Tag = 8192 });
                CmbOpenRouterThinkingTokens.Items.Add(new ComboBoxItem { Content = "16,384 tokens", Tag = 16384 });
                CmbOpenRouterThinkingTokens.Items.Add(new ComboBoxItem { Content = "32,768 tokens", Tag = 32768 });
            }

            int currentTokens = settings.OpenRouter.ReasoningMaxTokens;
            int tokenIdx = 0;
            for (int i = 0; i < CmbOpenRouterThinkingTokens.Items.Count; i++)
            {
                if (CmbOpenRouterThinkingTokens.Items[i] is ComboBoxItem item &&
                    item.Tag is int val && val == currentTokens)
                {
                    tokenIdx = i;
                    break;
                }
            }
            CmbOpenRouterThinkingTokens.SelectedIndex = tokenIdx;

            SliOpenRouterTemperature.Value = Math.Clamp(settings.OpenRouter.Temperature, 0.0, 1.5);
            LblOpenRouterTemperature.Text = $"Temperature — {settings.OpenRouter.Temperature:F2}";

            SliOpenRouterMaxTokens.Value = Math.Clamp(settings.OpenRouter.MaxTokens > 0 ? settings.OpenRouter.MaxTokens : 700, 256, 4096);
            LblOpenRouterMaxTokens.Text = $"Max Output Length — {(int)SliOpenRouterMaxTokens.Value} tokens";

            ChkOpenRouterFallbacks.IsChecked = settings.OpenRouter.AllowFallbacks;
            ChkOpenRouterPrivacy.IsChecked = string.Equals(settings.OpenRouter.DataCollection, "deny", StringComparison.OrdinalIgnoreCase);

            ApplyAiCompanionState();
        }

        private static string AnswerColorLabel(string key) => key switch
        {
            Models.CommandDockTextColors.Auto => Loc.Text("CommandDockColorAuto", "Theme colour"),
            Models.CommandDockTextColors.White => Loc.Text("CommandDockColorWhite", "White"),
            Models.CommandDockTextColors.Black => Loc.Text("CommandDockColorBlack", "Black"),
            Models.CommandDockTextColors.Cyan => Loc.Text("CommandDockColorCyan", "Cyan"),
            Models.CommandDockTextColors.Amber => Loc.Text("CommandDockColorAmber", "Amber"),
            Models.CommandDockTextColors.Red => Loc.Text("CommandDockColorRed", "Red"),
            Models.CommandDockTextColors.Green => Loc.Text("CommandDockColorGreen", "Green"),
            _ => key,
        };

        private string SelectedAiProvider =>
            (CmbAiProvider.SelectedItem as ComboBoxItem)?.Tag as string ?? AiProviders.OpenAi;

        /// <summary>
        /// Brings the panel in line with what the chosen provider can do and what has been set
        /// up so far. Everything that would otherwise be a setting quietly doing nothing gets
        /// disabled here with the reason beside it.
        /// </summary>
        private void ApplyAiCompanionState()
        {
            var provider = SelectedAiProvider;
            var settings = AiCompanionStore.Current;

            TxtAiProviderNote.Text = AiProviders.AcceptsAudio(provider)
                ? Loc.Text("AiCompanionProviderNative",
                    "Takes your recording directly, so what it hears is what you said.")
                : Loc.Text("AiCompanionProviderTranscribed",
                    "Does not accept audio. Your recording is turned into text on this PC first, which is slower and less accurate with names and game terms than GPT or Gemini.");

            TxtAiProviderNote.Text += " " + (AiProviders.HasVoice(provider)
                ? Loc.Text("AiCompanionVoiceNative",
                    "Spoken answers use this provider's own voice.")
                : Loc.Text("AiCompanionVoiceWindows",
                    "Spoken answers are read by Windows, which sounds noticeably more synthetic."));

            // What it costs to use, because none of the three is covered by the chat
            // subscription people already pay for and all three fail the same anonymous way
            // when the credit runs out.
            TxtAiProviderNote.Text += " " + (provider switch
            {
                AiProviders.Gemini => Loc.Text("AiCompanionBillingFree",
                    "Google gives the API a free allowance, so a key from AI Studio works without paying. Busy sessions can still hit the per-minute limit."),
                AiProviders.OpenRouter => Loc.Text("AiCompanionBillingOpenRouter",
                    "One key for many models with pay-as-you-go credit. Prices vary per model — cheap ones cost a fraction of GPT per question."),
                _ => Loc.Text("AiCompanionBillingPrepaid",
                    "This API is billed separately from any chat subscription and needs its own prepaid credit."),
            });

            // Per provider, and it says which of the others are set up too — the whole reason
            // there is a slot each is that people keep more than one and switch between them.
            bool hasKey = AiCompanionStore.HasKeyFor(provider);

            var others = AiProviders.All
                .Where(other => other != provider && AiCompanionStore.HasKeyFor(other))
                .Select(AiProviders.DisplayName)
                .ToList();

            TxtAiKeyState.Text = hasKey
                ? string.Format(Loc.Text("AiCompanionKeyStoredFor",
                    "A {0} key is stored on this PC."), AiProviders.DisplayName(provider))
                : string.Format(Loc.Text("AiCompanionKeyMissingFor",
                    "No {0} key yet — this provider cannot send anything without one."),
                    AiProviders.DisplayName(provider));

            if (others.Count > 0)
            {
                TxtAiKeyState.Text += " " + string.Format(
                    Loc.Text("AiCompanionKeyAlsoStored", "Also stored: {0}."),
                    string.Join(", ", others));
            }

            BtnRemoveAiKey.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;

            // Streaming needs two things now: spoken answers switched on at all, and a
            // supporter account. It used to need a provider with its own voice as well —
            // that stopped being true once the queue started speaking a sentence at a time,
            // which Windows can do as readily as GPT can.
            bool premium = SupabaseAuthManager.IsPremium;
            bool speaking = ChkAiAudioAnswers.IsChecked == true;

            ChkAiStreamingVoice.IsEnabled = premium && speaking;

            var reason =
                !speaking ? Loc.Text("AiCompanionStreamingNeedsVoice",
                    "Only applies when answers are read out loud.")
                : !premium ? Loc.Text("AiCompanionStreamingSupporter",
                    "Spoken answers start once the model has finished. Supporters hear them as they are written.")
                : Loc.Text("AiCompanionStreamingOn",
                    "The answer is spoken as it is written instead of after it is finished.");

            TxtAiAnswerNote.Text = reason;

            // On the switch as well, with ShowOnDisabled set in the markup: a greyed control is
            // exactly where someone points to ask why, and it has nothing to say by default.
            ToolTipService.SetToolTip(ChkAiStreamingVoice, reason);

            // The label follows the switch, so the row reads as unavailable rather than as a
            // live setting that simply refuses to move.
            LblAiStreamingVoice.Opacity = ChkAiStreamingVoice.IsEnabled ? 1.0 : 0.5;

            TxtAiHotkey.Text = string.IsNullOrWhiteSpace(settings.Hotkey)
                ? Loc.Text("AiCompanionHotkeyNone", "Not set — click the tile to record instead")
                : settings.Hotkey;

            // The box is per provider, so switching provider has to bring its own model with
            // it — otherwise a name typed for Gemini would be sent to Claude.
            _loadingAiModel = true;
            TxtAiModel.Text = settings.Models.TryGetValue(provider, out var model) ? model : "";
            _loadingAiModel = false;

            TxtAiModel.PlaceholderText = AiProviders.DefaultModel(provider);
            BtnAiModelReset.IsEnabled = !string.IsNullOrWhiteSpace(TxtAiModel.Text);

            // OpenRouter tools (browse all models / refresh catalog)
            if (PanelOpenRouterTools != null)
            {
                PanelOpenRouterTools.Visibility = provider == AiProviders.OpenRouter
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (PanelOpenRouterOptions != null)
            {
                PanelOpenRouterOptions.Visibility = provider == AiProviders.OpenRouter
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            // One-click choices for this provider. Hidden when there is nothing to offer;
            // picking one just fills the text field, which stays the saved value.
            RefreshAiModelChoices(provider, settings.Models.TryGetValue(provider, out var current) ? current : "");

            if (provider == AiProviders.OpenRouter && !string.IsNullOrWhiteSpace(TxtAiModel.Text))
            {
                var found = OpenRouterModelService.FindModel(TxtAiModel.Text);
                if (found != null)
                {
                    TxtAiModelNote.Text = $"{found.Name}\n" +
                        $"{(found.IsFree ? "🎁 100% Free model" : found.PricingSummary)}  ·  {found.FormattedContextLength}  ·  {(found.SupportsVision ? "👁️ Vision supported" : "💬 Text only")}" +
                        (string.IsNullOrWhiteSpace(found.Description) ? "" : $"\n{found.Description}");
                }
                else
                {
                    TxtAiModelNote.Text = string.Format(
                        Loc.Text("AiCompanionModelNote",
                            "Leave empty to use {0}. Change it if the provider replies that the model no longer exists."),
                        AiProviders.DefaultModel(provider));
                }
            }
            else
            {
                TxtAiModelNote.Text = string.Format(
                    Loc.Text("AiCompanionModelNote",
                        "Leave empty to use {0}. Change it if the provider replies that the model no longer exists."),
                    AiProviders.DefaultModel(provider));
            }

            // Only where there is a provider voice to name. With Claude the answer is read by
            // Windows whatever is typed here, so the field would be a setting that does nothing.
            PanelAiVoiceModel.Visibility = AiProviders.HasVoice(provider)
                ? Visibility.Visible
                : Visibility.Collapsed;

            _loadingAiModel = true;
            TxtAiVoiceModel.Text = settings.Models.TryGetValue(AiProviders.VoiceModelKey(provider), out var voice)
                ? voice
                : "";
            _loadingAiModel = false;

            TxtAiVoiceModel.PlaceholderText = AiProviders.DefaultVoiceModel(provider);
            BtnAiVoiceModelReset.IsEnabled = !string.IsNullOrWhiteSpace(TxtAiVoiceModel.Text);

            TxtAiVoiceModelNote.Text = string.Format(
                Loc.Text("AiCompanionVoiceModelNote",
                    "Leave empty to use {0}. If spoken answers come out in the Windows voice instead of this provider's, this name is the usual reason."),
                AiProviders.DefaultVoiceModel(provider));

            // The one place the quiet failure becomes visible.
            if (!string.IsNullOrEmpty(AiSpeech.LastError))
            {
                TxtAiVoiceModelNote.Text += " " + string.Format(
                    Loc.Text("AiCompanionVoiceLastError", "Last attempt: {0}"), AiSpeech.LastError);
            }

            // Said in terms of what it fixes and what it costs, because it is the one setting
            // here that meaningfully changes the bill.
            TxtAiGameDataNote.Text = settings.IncludeGameData
                ? (provider == AiProviders.Gemini
                    ? Loc.Text("AiCompanionGameDataOnFree",
                        "Current recipes and raid costs go with every question, so the model is not answering from an old patch. Free on Gemini.")
                    : Loc.Text("AiCompanionGameDataOn",
                        "Current recipes and raid costs go with every question, so the model is not answering from an old patch. Adds about 10,000 tokens a question, most of it cached after the first."))
                : Loc.Text("AiCompanionGameDataOff",
                    "Cheaper, but recipes and raid costs come from the model's memory — which holds every retired version of them and cannot tell which is current.");

            TxtAiAnswerLanguageNote.Text = settings.AnswerLanguage switch
            {
                AnswerLanguages.AppLanguage => Loc.Text("AiCompanionLanguageAppNote",
                    "Answers always come back in the app's language, even when you ask in another one."),
                AnswerLanguages.English => Loc.Text("AiCompanionLanguageEnglishNote",
                    "Always English. Useful because Rust's own item and monument names are English."),
                _ => string.Format(Loc.Text("AiCompanionLanguageMatchNote",
                    "Ask in English and the answer is English; ask in {0} and it is {0}. Falls back to {0} when the recording has no words in it."),
                    AiPrompt.CurrentLanguage()),
            };

            // Windows' voice picks a language when it is built, so a spoken answer that came
            // back in a different one is read with the wrong accent. The providers' own voices
            // follow the text, which is one more reason to prefer them.
            if (settings.AnswerLanguage == AnswerLanguages.MatchQuestion &&
                settings.AudioAnswers &&
                !AiProviders.HasVoice(provider))
            {
                TxtAiAnswerLanguageNote.Text += " " + Loc.Text("AiCompanionLanguageVoiceWarn",
                    "Windows reads every answer in one language, so an answer in another one will sound wrong.");
            }

            TxtAiShotZoomNote.Text = settings.ScreenshotZoom >= 0.99
                ? Loc.Text("AiCompanionShotFullNote",
                    "Everything you can see, including the HUD and the map. Small things far from the crosshair may be too small for the model to identify.")
                : Loc.Text("AiCompanionShotCropNote",
                    "Only what you are aiming at, at full resolution. Much better at naming a switch on a wall or an item on the ground, and it cannot see the rest of the screen.");

            UpdateAiAnswerLabels();
        }

        private bool _loadingAiModel;

        private void UpdateAiAnswerLabels()
        {
            LblAiAnswerOpacity.Text = string.Format(
                Loc.Text("AiCompanionAnswerOpacity", "Transparency — {0}% opaque"),
                (int)Math.Round(SliAiAnswerOpacity.Value * 100));

            LblAiAnswerWidth.Text = string.Format(
                Loc.Text("AiCompanionAnswerWidth", "Width — {0} px"),
                (int)SliAiAnswerWidth.Value);

            LblAiAnswerHeight.Text = string.Format(
                Loc.Text("AiCompanionAnswerHeight", "Height before it scrolls — {0} px"),
                (int)SliAiAnswerHeight.Value);
        }

        private void TxtAiModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isSettingsInitialized || _loadingAiModel) return;

            var settings = AiCompanionStore.Current;
            var provider = SelectedAiProvider;
            var typed = TxtAiModel.Text?.Trim() ?? "";

            // An empty box means the default, not an empty model name — stored as the absence
            // of an entry so a later change of default is picked up rather than pinned.
            if (typed.Length == 0) settings.Models.Remove(provider);
            else settings.Models[provider] = typed;

            AiCompanionStore.Save(settings);
            BtnAiModelReset.IsEnabled = typed.Length > 0;

            // Keep the one-click list in step with hand typing, without saving twice.
            if (CmbAiModel != null && CmbAiModel.Visibility == Visibility.Visible)
            {
                _loadingAiModel = true;
                try
                {
                    int match = 0;
                    for (int i = 1; i < CmbAiModel.Items.Count; i++)
                    {
                        if (string.Equals((CmbAiModel.Items[i] as ComboBoxItem)?.Tag as string,
                                typed, StringComparison.OrdinalIgnoreCase))
                        {
                            match = i;
                            break;
                        }
                    }
                    if (CmbAiModel.SelectedIndex != match) CmbAiModel.SelectedIndex = match;
                }
                finally
                {
                    _loadingAiModel = false;
                }
            }
        }

        private void BtnAiModelReset_Click(object sender, RoutedEventArgs e)
        {
            TxtAiModel.Text = "";
            if (CmbAiModel != null) CmbAiModel.SelectedIndex = 0;
            ApplyAiCompanionState();
        }

        private void TxtAiVoiceModel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isSettingsInitialized || _loadingAiModel) return;

            var settings = AiCompanionStore.Current;
            var key = AiProviders.VoiceModelKey(SelectedAiProvider);
            var typed = TxtAiVoiceModel.Text?.Trim() ?? "";

            if (typed.Length == 0) settings.Models.Remove(key);
            else settings.Models[key] = typed;

            AiCompanionStore.Save(settings);
            BtnAiVoiceModelReset.IsEnabled = typed.Length > 0;
        }

        private void BtnAiVoiceModelReset_Click(object sender, RoutedEventArgs e)
        {
            TxtAiVoiceModel.Text = "";
            ApplyAiCompanionState();
        }

        private bool _isFetchingOpenRouterModels;

        /// <summary>
        /// Rebuilds the one-click model list for this provider.
        ///
        /// For OpenRouter, dynamically populates all auto-fetched free models alongside popular presets.
        /// </summary>
        private void RefreshAiModelChoices(string provider, string current)
        {
            if (CmbAiModel == null) return;

            if (provider == AiProviders.OpenRouter)
            {
                PopulateOpenRouterModelChoices(current);
                return;
            }

            var suggestions = AiProviders.SuggestedModels(provider);
            if (suggestions.Length == 0)
            {
                CmbAiModel.Visibility = Visibility.Collapsed;
                return;
            }

            CmbAiModel.Visibility = Visibility.Visible;

            _loadingAiModel = true;
            try
            {
                CmbAiModel.Items.Clear();
                CmbAiModel.Items.Add(new ComboBoxItem
                {
                    Content = string.Format(
                        Loc.Text("AiCompanionModelDefaultChoice", "Default ({0})"),
                        AiProviders.DefaultModel(provider)),
                    Tag = "",
                });
                foreach (var name in suggestions)
                    CmbAiModel.Items.Add(new ComboBoxItem { Content = name, Tag = name });

                var trimmed = (current ?? "").Trim();
                int match = 0;
                for (int i = 1; i < CmbAiModel.Items.Count; i++)
                {
                    if (string.Equals((CmbAiModel.Items[i] as ComboBoxItem)?.Tag as string,
                            trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        match = i;
                        break;
                    }
                }

                // A hand-typed id with no suggestion keeps the default row selected rather
                // than inventing a row for it — the text field shows what will be used.
                CmbAiModel.SelectedIndex = match;
            }
            finally
            {
                _loadingAiModel = false;
            }
        }

        private void PopulateOpenRouterModelChoices(string current)
        {
            CmbAiModel.Visibility = Visibility.Visible;

            // Trigger background fetch if not already done
            if (!_isFetchingOpenRouterModels)
            {
                _isFetchingOpenRouterModels = true;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await OpenRouterModelService.GetModelsAsync();
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (SelectedAiProvider == AiProviders.OpenRouter)
                            {
                                PopulateOpenRouterModelChoices(TxtAiModel?.Text ?? "");
                                ApplyAiCompanionState();
                            }
                        });
                    }
                    finally
                    {
                        _isFetchingOpenRouterModels = false;
                    }
                });
            }

            _loadingAiModel = true;
            try
            {
                CmbAiModel.Items.Clear();

                // 1. Default Option
                CmbAiModel.Items.Add(new ComboBoxItem
                {
                    Content = string.Format(
                        Loc.Text("AiCompanionModelDefaultChoice", "Default ({0})"),
                        AiProviders.DefaultModel(AiProviders.OpenRouter)),
                    Tag = "",
                });

                var trimmed = (current ?? "").Trim();
                bool currentMatched = string.IsNullOrEmpty(trimmed);

                // 2. Free Models Section (from non-blocking cache)
                var freeModels = OpenRouterModelService.GetCachedFreeModels();
                if (freeModels.Count > 0)
                {
                    CmbAiModel.Items.Add(new ComboBoxItem
                    {
                        Content = $"─── 🎁 Free Models ({freeModels.Count}) ───",
                        IsEnabled = false,
                        FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.MediumSeaGreen,
                    });

                    foreach (var model in freeModels)
                    {
                        var item = new ComboBoxItem
                        {
                            Content = $"[Free] {model.Name} ({model.Id})",
                            Tag = model.Id,
                        };
                        CmbAiModel.Items.Add(item);
                        if (!currentMatched && string.Equals(model.Id, trimmed, StringComparison.OrdinalIgnoreCase))
                            currentMatched = true;
                    }
                }

                // 3. Popular Models Section
                var popular = AiProviders.SuggestedModels(AiProviders.OpenRouter);
                if (popular.Length > 0)
                {
                    CmbAiModel.Items.Add(new ComboBoxItem
                    {
                        Content = "─── ⚡ Popular Models ───",
                        IsEnabled = false,
                        FontWeight = FontWeights.Bold,
                    });

                    foreach (var id in popular)
                    {
                        if (freeModels.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var item = new ComboBoxItem
                        {
                            Content = id,
                            Tag = id,
                        };
                        CmbAiModel.Items.Add(item);
                        if (!currentMatched && string.Equals(id, trimmed, StringComparison.OrdinalIgnoreCase))
                            currentMatched = true;
                    }
                }

                // If current model is a custom/searched one not in list, add it so selection works
                if (!currentMatched && !string.IsNullOrEmpty(trimmed))
                {
                    var customItem = new ComboBoxItem
                    {
                        Content = $"Selected: {trimmed}",
                        Tag = trimmed,
                    };
                    CmbAiModel.Items.Add(customItem);
                }

                // Select matching item
                int match = 0;
                for (int i = 0; i < CmbAiModel.Items.Count; i++)
                {
                    if (CmbAiModel.Items[i] is ComboBoxItem cbi && cbi.IsEnabled &&
                        string.Equals(cbi.Tag as string, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        match = i;
                        break;
                    }
                }

                CmbAiModel.SelectedIndex = match;
            }
            finally
            {
                _loadingAiModel = false;
            }
        }

        private void BtnBrowseOpenRouterModels_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.OpenRouterModelPickerWindow(TxtAiModel.Text)
            {
                Owner = Window.GetWindow(this),
            };

            if (picker.ShowDialog() == true && !string.IsNullOrWhiteSpace(picker.SelectedModelId))
            {
                TxtAiModel.Text = picker.SelectedModelId;
                ApplyAiCompanionState();
            }
        }

        private async void BtnRefreshOpenRouterModels_Click(object sender, RoutedEventArgs e)
        {
            if (BtnRefreshOpenRouterModels != null) BtnRefreshOpenRouterModels.IsEnabled = false;
            try
            {
                await OpenRouterModelService.GetModelsAsync(forceRefresh: true);
                PopulateOpenRouterModelChoices(TxtAiModel.Text);
                ApplyAiCompanionState();
            }
            finally
            {
                if (BtnRefreshOpenRouterModels != null) BtnRefreshOpenRouterModels.IsEnabled = true;
            }
        }

        private void CmbAiModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized || _loadingAiModel) return;
            if (CmbAiModel?.SelectedItem is not ComboBoxItem picked || !picked.IsEnabled) return;

            // Default row clears the override; anything else fills the text field, whose
            // own change handler does the saving.
            TxtAiModel.Text = (picked.Tag as string) ?? "";
        }

        private void OnAiAnswerAppearanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isSettingsInitialized) return;
            if (SliAiAnswerOpacity == null || SliAiAnswerWidth == null || SliAiAnswerHeight == null) return;

            var settings = AiCompanionStore.Current;
            settings.AnswerOpacity = SliAiAnswerOpacity.Value;
            settings.AnswerWidth = SliAiAnswerWidth.Value;
            settings.AnswerHeight = SliAiAnswerHeight.Value;
            AiCompanionStore.Save(settings);

            UpdateAiAnswerLabels();
        }

        private void CmbAiAnswerColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            var settings = AiCompanionStore.Current;
            settings.AnswerTextColorKey =
                (CmbAiAnswerColor.SelectedItem as ComboBoxItem)?.Tag as string
                ?? Models.CommandDockTextColors.Auto;
            AiCompanionStore.Save(settings);
        }

        /// <summary>
        /// How much of the screen a screenshot keeps.
        ///
        /// Three steps rather than a slider: the choice is between wanting the situation and
        /// wanting the detail, and there is no useful answer at 43 per cent.
        /// </summary>
        private static readonly (double Zoom, Func<string> Label)[] ShotZooms =
        {
            (1.0, () => Loc.Text("AiCompanionShotFull", "Whole screen")),
            (0.5, () => Loc.Text("AiCompanionShotHalf", "Middle half — closer look")),
            (0.25, () => Loc.Text("AiCompanionShotQuarter", "Middle quarter — closest look")),
        };

        /// <summary>
        /// What language answers come back in.
        ///
        /// Following the question is first and is the default: Rust's own vocabulary is
        /// English, so asking in English while running the app in another language is the
        /// normal case rather than the exception.
        /// </summary>
        private static readonly (string Key, Func<string> Label)[] AnswerLanguageChoices =
        {
            (AnswerLanguages.MatchQuestion,
                () => Loc.Text("AiCompanionLanguageMatch", "Same language as the question")),
            (AnswerLanguages.AppLanguage,
                () => string.Format(Loc.Text("AiCompanionLanguageApp", "App language ({0})"),
                    AiPrompt.CurrentLanguage())),
            (AnswerLanguages.English,
                () => Loc.Text("AiCompanionLanguageEnglish", "Always English")),
        };

        private void CmbAiAnswerLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            var settings = AiCompanionStore.Current;
            settings.AnswerLanguage =
                (CmbAiAnswerLanguage.SelectedItem as ComboBoxItem)?.Tag as string
                ?? AnswerLanguages.MatchQuestion;
            AiCompanionStore.Save(settings);

            ApplyAiCompanionState();
        }

        private void CmbAiShotZoom_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            var settings = AiCompanionStore.Current;
            settings.ScreenshotZoom = (CmbAiShotZoom.SelectedItem as ComboBoxItem)?.Tag as double? ?? 1.0;
            AiCompanionStore.Save(settings);

            ApplyAiCompanionState();
        }

        private void BtnAiAnswerPreview_Click(object sender, RoutedEventArgs e)
            => Windows.AiAnswerWindow.ShowPreview(Window.GetWindow(this));

        private void BtnAiHistory_Click(object sender, RoutedEventArgs e)
        {
            var history = new Windows.AiHistoryWindow { Owner = Window.GetWindow(this) };
            history.ShowDialog();
        }

        private void CmbAiProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            var settings = AiCompanionStore.Current;
            settings.Provider = SelectedAiProvider;
            AiCompanionStore.Save(settings);


            ApplyAiCompanionState();
        }

        private void OnAiSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            if (ChkAiTextAnswers == null || ChkAiAudioAnswers == null) return;

            // One of the two has to stay on, or an answer arrives with nowhere to go. The box
            // the user just cleared is the one that gives way.
            if (ChkAiTextAnswers.IsChecked != true && ChkAiAudioAnswers.IsChecked != true)
            {
                if (ReferenceEquals(sender, ChkAiTextAnswers)) ChkAiAudioAnswers.IsChecked = true;
                else ChkAiTextAnswers.IsChecked = true;
            }

            var settings = AiCompanionStore.Current;
            settings.TextAnswers = ChkAiTextAnswers.IsChecked == true;
            settings.AudioAnswers = ChkAiAudioAnswers.IsChecked == true;
            settings.StreamingVoice = ChkAiStreamingVoice.IsChecked == true;
            settings.CaptureGameAudio = ChkAiGameAudio.IsChecked == true;
            settings.AttachScreenshotByDefault = ChkAiScreenshotDefault.IsChecked == true;
            settings.AutoSendAfterRecording = ChkAiAutoSend.IsChecked == true;
            settings.IncludeGameData = ChkAiGameData.IsChecked == true;
            AiCompanionStore.Save(settings);

            ApplyAiCompanionState();
        }

        private void OnOpenRouterOptionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            var settings = AiCompanionStore.Current;

            if (CmbOpenRouterReasoning?.SelectedItem is ComboBoxItem reasoningItem && reasoningItem.Tag is string effort)
            {
                settings.OpenRouter.ReasoningEffort = effort;
            }

            if (CmbOpenRouterThinkingTokens?.SelectedItem is ComboBoxItem tokenItem && tokenItem.Tag is int tokens)
            {
                settings.OpenRouter.ReasoningMaxTokens = tokens;
            }

            AiCompanionStore.Save(settings);
        }

        private void OnOpenRouterSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isSettingsInitialized) return;
            var settings = AiCompanionStore.Current;

            if (SliOpenRouterTemperature != null && LblOpenRouterTemperature != null)
            {
                double temp = Math.Round(SliOpenRouterTemperature.Value, 2);
                settings.OpenRouter.Temperature = temp;
                LblOpenRouterTemperature.Text = $"Temperature — {temp:F2}";
            }

            if (SliOpenRouterMaxTokens != null && LblOpenRouterMaxTokens != null)
            {
                int maxTokens = (int)Math.Round(SliOpenRouterMaxTokens.Value);
                settings.OpenRouter.MaxTokens = maxTokens;
                LblOpenRouterMaxTokens.Text = $"Max Output Length — {maxTokens} tokens";
            }

            AiCompanionStore.Save(settings);
        }

        private void OnOpenRouterSwitchChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            var settings = AiCompanionStore.Current;

            if (ChkOpenRouterFallbacks != null)
            {
                settings.OpenRouter.AllowFallbacks = ChkOpenRouterFallbacks.IsChecked == true;
            }

            if (ChkOpenRouterPrivacy != null)
            {
                settings.OpenRouter.DataCollection = ChkOpenRouterPrivacy.IsChecked == true ? "deny" : "allow";
            }

            AiCompanionStore.Save(settings);
        }

        private async void BtnSaveAiKey_Click(object sender, RoutedEventArgs e)
        {
            var key = TxtAiKey.Password;
            var provider = SelectedAiProvider;

            if (!AiProviders.LooksLikeKey(provider, key))
            {
                await ShowAiMessage(
                    Loc.Text("AiCompanionKeyRejectedTitle", "That does not look like a key"),
                    string.Format(
                        Loc.Text("AiCompanionKeyRejected",
                            "A {0} key does not look like that. Check you pasted the whole thing, without the surrounding quotes."),
                        AiProviders.DisplayName(provider)));
                return;
            }

            // The policy is shown before the first key is ever written, not after, and the field
            // stays empty if it is declined.
            if (AiCompanionStore.Current.PolicyAcceptedUtc == null && !await AcceptAiPolicyAsync())
                return;

            // Against the provider shown in the dropdown, not against whatever the settings
            // happen to hold: the two are kept in step, and being explicit is what makes it
            // safe to keep a key for each of the three at once.
            AiCompanionStore.WriteKeyFor(provider, key);
            TxtAiKey.Password = "";
            ApplyAiCompanionState();
        }

        private async void BtnRemoveAiKey_Click(object sender, RoutedEventArgs e)
        {
            var box = new WpfUi.MessageBox
            {
                Title = Loc.Text("AiCompanionRemoveKey", "Remove key"),
                Content = Loc.Text("AiCompanionRemoveKeyConfirm",
                    "Remove the stored key from this PC? The companion stops working until a new one is entered. Nothing is changed at your provider — revoke it there too if it may have leaked."),
                PrimaryButtonText = Loc.Text("AiCompanionRemoveKey", "Remove key"),
                CloseButtonText = Loc.Text("Cancel", "Cancel"),
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (await box.ShowDialogAsync() != WpfUi.MessageBoxResult.Primary) return;

            // Only this provider's. The other two are separate accounts and separate bills.
            AiCompanionStore.WriteKeyFor(SelectedAiProvider, null);
            ApplyAiCompanionState();
        }

        private async void BtnAiPolicy_Click(object sender, RoutedEventArgs e) => await AcceptAiPolicyAsync();

        /// <summary>
        /// What happens to a recording, in the plainest words available, with acceptance recorded.
        /// Shown before the first key is stored and readable again from the button at any time.
        /// </summary>
        private async System.Threading.Tasks.Task<bool> AcceptAiPolicyAsync()
        {
            // OpenRouter is a router, so "straight to the provider whose key you entered" is
            // true of the first hop and quietly wrong about the rest: the request is passed on
            // to whichever company actually serves the chosen model, under terms the user has
            // never seen. That is worth a paragraph in a dialog whose whole purpose is telling
            // someone where their voice and their screen are about to go.
            //
            // Its own key rather than an edit to the policy text, because the policy exists in
            // every language the app ships and a rewrite would leave most of them describing
            // the old behaviour. A new key falls back to English instead.
            var routing = SelectedAiProvider == AiProviders.OpenRouter
                ? Environment.NewLine + Environment.NewLine + Loc.Text("AiCompanionPolicyRouting",
                    "OpenRouter is a router, not the model itself: it passes your recording on to " +
                    "whichever company serves the model you picked, and that company's terms apply " +
                    "to it as well. Unless Strict Privacy is switched on, that can include providers " +
                    "who keep prompts and may train on them — which is generally why a model is " +
                    "free.")
                : "";

            var box = new WpfUi.MessageBox
            {
                Title = Loc.Text("AiCompanionPolicyTitle", "What happens to your recordings"),
                Content = Loc.Text("AiCompanionPolicy",
                    "The recording is made on this PC and sent straight to the AI provider whose key you entered. "
                    + "It does not pass through our servers and we never receive it.\n\n"
                    + "What happens to it after that is governed by that provider's terms — the ones you agreed to when you created the key, not ours.\n\n"
                    + "Your key is stored only on this PC, protected by your Windows account, and is never uploaded. "
                    + "Because it is tied to this Windows account, it will not survive a reinstall or move to another PC: you will need to enter it again.\n\n"
                    + "Recording only ever runs while you hold the hotkey or after you click the tile, and stops by itself after five minutes.")
                    + routing,
                PrimaryButtonText = Loc.Text("AiCompanionPolicyAccept", "Understood"),
                CloseButtonText = Loc.Text("Cancel", "Cancel"),
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };

            if (await box.ShowDialogAsync() != WpfUi.MessageBoxResult.Primary) return false;

            var settings = AiCompanionStore.Current;
            settings.PolicyAcceptedUtc = DateTime.UtcNow;
            AiCompanionStore.Save(settings);
            return true;
        }

        private void BtnAiHotkey_Click(object sender, RoutedEventArgs e)
        {
            var capture = new Windows.HotkeyCaptureWindow { Owner = Window.GetWindow(this), AllowClear = true };
            if (capture.ShowDialog() != true) return;

            // An empty gesture clears it, and having no hotkey is a valid choice: the tile can
            // be clicked instead, which is what the label says when there is none.
            var settings = AiCompanionStore.Current;
            settings.Hotkey = capture.Gesture?.Trim() ?? "";
            AiCompanionStore.Save(settings);

            // Registered right away rather than at the next restart: the user has just chosen
            // a key combination and the obvious next thing they do is try it.
            (Application.Current?.MainWindow as Views.MainWindow)?.ApplyAiHotkey();

            ApplyAiCompanionState();
        }

        private async System.Threading.Tasks.Task ShowAiMessage(string title, string message)
        {
            var box = new WpfUi.MessageBox
            {
                Title = title,
                Content = message,
                PrimaryButtonText = Properties.Resources.OK,
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            await box.ShowDialogAsync();
        }
    }
}
