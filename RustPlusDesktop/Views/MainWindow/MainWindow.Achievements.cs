using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RustPlusDesk.Services.Achievements;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _achievementsWired;

    /// <summary>
    /// Hooks the achievement service up to the button and the snackbar. Called once
    /// during startup; everything after that is event-driven, so nothing polls.
    /// </summary>
    private void WireAchievements()
    {
        if (_achievementsWired) return;
        _achievementsWired = true;

        AchievementService.Unlocked += OnAchievementUnlocked;
        AchievementService.UnseenChanged += OnAchievementUnseenChanged;

        UpdateAchievementBadge(AchievementService.UnseenCount);

        // Connecting, restoring the team, reading the device list and reloading the
        // cloud state all happen in the first seconds and can each earn something.
        // Those are recorded silently; toasts start once the app has settled.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(12));
            AchievementService.SuppressUnlockedEvents = false;
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnAchievementUnlocked(AchievementDef def)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                ShowInfoSnackbar(
                    Helpers.Loc.Text("AchievementUnlocked", "Achievement unlocked"),
                    def.Name,
                    WpfUi.ControlAppearance.Success,
                    WpfUi.SymbolRegular.Trophy24);

                FlashAchievementButton();

                if (AchievementService.AllEarned) OfferAllAchievementsTicket();
            }
            catch
            {
                // Never let a bit of celebration break whatever earned it.
            }
        });
    }

    private bool _allAchievementsPromptShown;

    /// <summary>
    /// Asks, once, whether to tell us about a full set.
    ///
    /// This one does not fade: it is a question, and a prompt that vanishes while
    /// someone is mid-raid is a prompt they never answer. Dismissing it counts as
    /// no, which is why there is no separate "No" handler - closing is the answer.
    /// </summary>
    private void OfferAllAchievementsTicket()
    {
        if (_allAchievementsPromptShown) return;
        if (Services.TrackingService.AllAchievementsTicketOffered) return;
        _allAchievementsPromptShown = true;

        var stack = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Vertical
        };

        stack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Helpers.Loc.Text(
                "AchievementsAllUnlockedBody",
                "All Achievements unlocked. Well done! Let the developers know?"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal
        };

        var yes = new WpfUi.Button
        {
            Content = Helpers.Loc.Text("Yes", "Yes"),
            Appearance = WpfUi.ControlAppearance.Primary,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var no = new WpfUi.Button
        {
            Content = Helpers.Loc.Text("No", "No"),
            Appearance = WpfUi.ControlAppearance.Secondary
        };

        var item = new Controls.ToastItem
        {
            Title = Helpers.Loc.Text("AchievementsAllUnlockedTitle", "All Achievements unlocked"),
            Icon = WpfUi.SymbolRegular.Trophy24,
            AccentBrush = ToastAccentBrush(WpfUi.ControlAppearance.Success),
            MaxCardWidth = 460,
            Timeout = TimeSpan.Zero,   // stays until answered
        };

        yes.Click += async (_, _) =>
        {
            yes.IsEnabled = false;
            no.IsEnabled = false;
            RememberAllAchievementsOffered();
            RemoveToast(item);
            await SendAllAchievementsTicketAsync();
        };

        no.Click += (_, _) =>
        {
            RememberAllAchievementsOffered();
            RemoveToast(item);
        };

        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        stack.Children.Add(buttons);
        item.Content = stack;

        // Closing the card is the same answer as No, so it is not asked again.
        item.Closed = RememberAllAchievementsOffered;

        AddToast(item);
    }

    private static void RememberAllAchievementsOffered()
        => Services.TrackingService.AllAchievementsTicketOffered = true;

    /// <summary>
    /// Opens a support ticket exactly as the player would by hand, so replies reach
    /// them through the normal ticket notifications.
    /// </summary>
    private async System.Threading.Tasks.Task SendAllAchievementsTicketAsync()
    {
        try
        {
            string subject = "All Achievements unlocked";
            string body =
                $"All {AchievementService.TotalCount} achievements unlocked.\n\n" +
                "Sent from the achievements screen.";

            var meta = new Dictionary<string, string>
            {
                ["app_version"] = Helpers.VersionHelper.GetClientVersion(),
                ["os"] = Environment.OSVersion.VersionString,
                ["achievements_earned"] = AchievementService.EarnedCount.ToString(),
                ["achievements_total"] = AchievementService.TotalCount.ToString(),
            };

            bool ok = await Services.Support.SupportApi
                .CreateTicketAsync("other", subject, body, meta)
                .ConfigureAwait(true);

            ShowInfoSnackbar(
                Helpers.Loc.Text("AchievementsAllUnlockedTitle", "All Achievements unlocked"),
                ok
                    ? Helpers.Loc.Text("AchievementsTicketSent", "Sent - we will get back to you.")
                    : Helpers.Loc.Text("AchievementsTicketFailed", "That could not be sent. You can open a ticket from Support instead."),
                ok ? WpfUi.ControlAppearance.Success : WpfUi.ControlAppearance.Caution,
                WpfUi.SymbolRegular.Trophy24);
        }
        catch (Exception ex)
        {
            AppendLog($"[Achievements] Ticket failed: {ex.Message}");
        }
    }

    private void OnAchievementUnseenChanged(int count)
        => Dispatcher.InvokeAsync(() => UpdateAchievementBadge(count));

    private void UpdateAchievementBadge(int count)
    {
        if (AchievementBadge == null || TxtAchievementBadge == null) return;

        if (count <= 0)
        {
            AchievementBadge.Visibility = Visibility.Collapsed;
            return;
        }

        TxtAchievementBadge.Text = count > 9 ? "9+" : count.ToString();
        AchievementBadge.Visibility = Visibility.Visible;
    }

    /// <summary>A short glow, so something earned mid-game is noticed without a dialog.</summary>
    private void FlashAchievementButton()
    {
        if (BtnAchievements == null) return;

        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.35,
            Duration = TimeSpan.FromMilliseconds(320),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop
        };

        BtnAchievements.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void BtnAchievements_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new Windows.AchievementsWindow(this);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            AppendLog($"[Achievements] Could not open: {ex.Message}");
        }
    }

    /// <summary>
    /// Two finished tutorials earns it. Asks the progress store rather than counting
    /// events, so tutorials finished in an earlier session count as well.
    /// </summary>
    private async System.Threading.Tasks.Task CheckTutorialAchievementAsync()
    {
        try
        {
            if (_tutorialRegistry == null || _tutorialProgressStore == null) return;

            int done = 0;
            foreach (var def in _tutorialRegistry.Tutorials)
            {
                var progress = await _tutorialProgressStore.GetAsync(def);
                if (progress?.CompletedAtUtc != null) done++;
                if (done >= 2) break;
            }

            if (done >= 2) Ach.Unlock(Ach.Tutorials);
        }
        catch
        {
            // An achievement is never worth surfacing an error for.
        }
    }
}
