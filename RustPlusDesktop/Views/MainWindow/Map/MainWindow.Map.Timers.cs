using RustPlusDesk.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    // CUSTOM TIMER LOGIC

    private void BtnCustomTimer_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected != null)
        {
            ListActiveTimers.ItemsSource = _vm.Selected.CustomTimers;
            PopupCustomTimer.IsOpen = true;
        }
    }

    private void BtnDeleteTimer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id && _vm.Selected != null)
        {
            var timer = _vm.Selected.CustomTimers.FirstOrDefault(t => t.Id == id);
            if (timer != null)
            {
                _vm.Selected.CustomTimers.Remove(timer);
            }
        }
    }

    private void TxtTimerName_TextChanged(object sender, TextChangedEventArgs e)
    {
        var safeCommand = new string(TxtTimerName.Text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLower();
        TxtTimerCommandPreview.Text = safeCommand;
    }

    private void BtnAddTimer_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected == null) return;
        if (_vm.Selected.CustomTimers.Count >= 5)
        {
            MessageBox.Show("Maximum of 5 custom timers allowed.", "Timer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string name = TxtTimerName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        double hours = int.TryParse(TxtTimerHours.Text, out int h) ? h : 0;
        double mins = int.TryParse(TxtTimerMinutes.Text, out int m) ? m : 0;
        
        if (hours == 0 && mins == 0) return;

        var cmd = TxtTimerCommandPreview.Text;
        if (string.IsNullOrWhiteSpace(cmd)) cmd = name.ToLower();

        double totalMins = hours * 60 + mins;
        var timer = new CustomTimer
        {
            Name = name,
            Command = cmd,
            EndTimeUtc = DateTime.UtcNow.AddMinutes(totalMins),
            CreatedNotified = false,
            Notified60 = totalMins <= 60,
            Notified30 = totalMins <= 30,
            Notified10 = totalMins <= 10,
            Notified3 = totalMins <= 3
        };

        _vm.Selected.CustomTimers.Add(timer);
        
        TxtTimerName.Text = "";
        TxtTimerHours.Text = "";
        TxtTimerMinutes.Text = "";
        
        PopupCustomTimer.IsOpen = false;
        
        // Output chat creation string
        if (_vm.Selected.AlertCustomTimer)
        {
            var msg = string.Format(Properties.Resources.TimerCreated, _vm.Selected.ChatCommandPrefix + cmd, (int)hours, (int)mins);
            _ = SendTeamChatSafeAsync(msg);
        }
    }

    private void CheckCustomTimers()
    {
        if (_vm.Selected == null) return;
        
        bool anyCritical = false;
        var toRemove = new List<CustomTimer>();
        var now = DateTime.UtcNow;

        // First tick: silently purge any timers already expired from a previous session
        if (!_timerStartupCleanupDone)
        {
            _timerStartupCleanupDone = true;
            foreach (var t in _vm.Selected.CustomTimers.ToList())
            {
                if ((t.EndTimeUtc - now).TotalSeconds <= 0)
                {
                    toRemove.Add(t);
                }
            }
            foreach (var r in toRemove) _vm.Selected.CustomTimers.Remove(r);
            toRemove.Clear();
            if (_vm.Selected.CustomTimers.Count == 0) return;
        }

        foreach (var timer in _vm.Selected.CustomTimers.ToList())
        {
            var remaining = timer.EndTimeUtc - now;
            
            // Force UI update for binding
            timer.EndTimeUtc = timer.EndTimeUtc;
            timer.RefreshRemainingTime(); 

            if (remaining.TotalSeconds <= 0)
            {
                if (timer.SnoozedUntilUtc.HasValue)
                {
                    if (now >= timer.SnoozedUntilUtc.Value)
                    {
                        timer.SnoozedUntilUtc = now.AddMinutes(_vm.Selected.TimerAlarmSnoozeMinutes);
                        PlayTimerAlarm();
                        ShowTimerExpiredSnackbar(timer.Name);
                    }
                    continue;
                }

                if (_vm.Selected.AlertCustomTimer && remaining.TotalSeconds >= -60)
                {
                    _ = SendTeamChatSafeAsync($"{timer.Name}: 00:00");
                }
                if (!timer.AlarmPlayed)
                {
                    timer.AlarmPlayed = true;
                    PlayTimerAlarm();
                    ShowTimerExpiredSnackbar(timer.Name);

                    if (_vm.Selected.TimerAlarmSnoozeMinutes > 0)
                    {
                        timer.SnoozedUntilUtc = now.AddMinutes(_vm.Selected.TimerAlarmSnoozeMinutes);
                    }
                    else
                    {
                        toRemove.Add(timer);
                    }
                }
                continue;
            }

            if (remaining.TotalMinutes < 5)
            {
                anyCritical = true;
            }

            if (_vm.Selected.AlertCustomTimer)
            {
                if (remaining.TotalMinutes <= 60 && !timer.Notified60)
                {
                    timer.Notified60 = true;
                    if (remaining.TotalMinutes >= 59)
                    {
                        _ = SendTeamChatSafeAsync($"{timer.Name}: 60:00");
                    }
                }
                if (remaining.TotalMinutes <= 30 && !timer.Notified30)
                {
                    timer.Notified30 = true;
                    if (remaining.TotalMinutes >= 29)
                    {
                        _ = SendTeamChatSafeAsync($"{timer.Name}: 30:00");
                    }
                }
                if (remaining.TotalMinutes <= 10 && !timer.Notified10)
                {
                    timer.Notified10 = true;
                    if (remaining.TotalMinutes >= 9)
                    {
                        _ = SendTeamChatSafeAsync($"{timer.Name}: 10:00");
                    }
                }
                if (remaining.TotalMinutes <= 3 && !timer.Notified3)
                {
                    timer.Notified3 = true;
                    if (remaining.TotalMinutes >= 2)
                    {
                        _ = SendTeamChatSafeAsync($"{timer.Name}: 03:00");
                    }
                }
            }
        }

        foreach (var r in toRemove)
        {
            _vm.Selected.CustomTimers.Remove(r);
        }

        if (_vm.Selected.CustomTimers.Count > 0)
        {
            if (anyCritical)
            {
                if (IconCustomTimer.Foreground.IsFrozen)
                    IconCustomTimer.Foreground = new SolidColorBrush(((SolidColorBrush)IconCustomTimer.Foreground).Color);

                var anim = new System.Windows.Media.Animation.ColorAnimation
                {
                    From = Colors.Orange,
                    To = Colors.LimeGreen,
                    Duration = TimeSpan.FromSeconds(1),
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                };
                IconCustomTimer.Foreground.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
            else
            {
                if (!IconCustomTimer.Foreground.IsFrozen)
                    IconCustomTimer.Foreground.BeginAnimation(SolidColorBrush.ColorProperty, null);
                IconCustomTimer.Foreground = Brushes.LimeGreen;
            }
        }
        else
        {
            if (!IconCustomTimer.Foreground.IsFrozen)
                IconCustomTimer.Foreground.BeginAnimation(SolidColorBrush.ColorProperty, null);
            IconCustomTimer.Foreground = (Brush)FindResource("TextPrimary");
        }
    }

    public void StopTimerAlarm()
    {
        Dispatcher.Invoke(() =>
        {
            if (_timerAlarmPlayer != null)
            {
                _timerAlarmPlayer.Stop();
                _timerAlarmPlayer.Close();
                _timerAlarmPlayer = null;
                _timerAlarmFilePath = null;
                if (BtnStopTimerAlarm != null) BtnStopTimerAlarm.Visibility = Visibility.Collapsed;
                if (BtnSnoozeTimerAlarm != null) BtnSnoozeTimerAlarm.Visibility = Visibility.Collapsed;
                AppendLog("[timer-alarm] Stopped timer alarm audio.");
            }
        });
    }

    private async void PlayTimerAlarm()
    {
        if (_vm.Selected == null || !_vm.Selected.TimerAlarmEnabled) return;

        try
        {
            string audioFile;

            if (!string.IsNullOrWhiteSpace(_vm.Selected.TimerAlarmAudioPath))
            {
                audioFile = _vm.Selected.TimerAlarmAudioPath;
            }
            else
            {
                string baseDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                audioFile = System.IO.Path.Combine(baseDir, "Assets", "bell.mp3");
            }

            if (System.IO.File.Exists(audioFile))
            {
                var fullPath = System.IO.Path.GetFullPath(audioFile);
                Dispatcher.Invoke(() =>
                {
                    StopTimerAlarm();
                    _timerAlarmPlayer = new System.Windows.Media.MediaPlayer();
                    _timerAlarmPlayer.MediaFailed += (s, e) => AppendLog($"[timer-alarm] Media Failed: {e.ErrorException?.Message}");
                    _timerAlarmPlayer.MediaEnded += (s, e) => AppendLog("[timer-alarm] Playback ended.");
                    _timerAlarmFilePath = fullPath;
                    _timerAlarmPlayer.Open(new Uri(fullPath, UriKind.Absolute));
                    _timerAlarmPlayer.Volume = 1.0;
                    _timerAlarmPlayer.Play();
                    if (BtnStopTimerAlarm != null) BtnStopTimerAlarm.Visibility = Visibility.Visible;
                    if (BtnSnoozeTimerAlarm != null) BtnSnoozeTimerAlarm.Visibility = Visibility.Visible;
                    AppendLog($"[timer-alarm] Playing: {fullPath}");
                });
            }
            else
            {
                int duration = _vm.Selected.TimerAlarmBeepDurationSeconds;
                for (int i = 0; i < duration; i++)
                {
                    System.Media.SystemSounds.Beep.Play();
                    await Task.Delay(1000);
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[timer-alarm] Error playing audio: {ex.Message}");
            try { System.Media.SystemSounds.Beep.Play(); } catch { }
        }
    }

    private void BtnStopTimerAlarm_Click(object sender, RoutedEventArgs e)
    {
        DismissAlarm();
    }

    public void DismissAlarm()
    {
        StopTimerAlarm();
        if (_vm.Selected != null)
        {
            foreach (var timer in _vm.Selected.CustomTimers)
            {
                timer.SnoozedUntilUtc = null;
            }
        }
    }

    private void BtnSnoozeTimerAlarm_Click(object sender, RoutedEventArgs e)
    {
        SnoozeAlarm();
    }

    public void SnoozeAlarm()
    {
        if (_vm.Selected == null) return;
        StopTimerAlarm();
        int snoozeMins = _vm.Selected.TimerAlarmSnoozeMinutes;
        var snoozedUntil = DateTime.UtcNow.AddMinutes(snoozeMins);
        foreach (var timer in _vm.Selected.CustomTimers)
        {
            if (timer.AlarmPlayed || timer.SnoozedUntilUtc.HasValue)
            {
                timer.SnoozedUntilUtc = snoozedUntil;
            }
        }
        AppendLog($"[timer-alarm] Snoozed for {snoozeMins} min.");
    }

    private void BtnSelectTimerAlarmAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav|All Files|*.*",
            Title = "Select Timer Alarm Sound"
        };

        if (dlg.ShowDialog() == true)
        {
            _vm.Selected.TimerAlarmAudioPath = dlg.FileName;
            _vm.Save();
            AppendLog($"[timer-alarm] Selected audio: {dlg.SafeFileName}");
        }
    }

    private void BtnResetTimerAlarmAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected == null) return;
        _vm.Selected.TimerAlarmAudioPath = null;
        _vm.Save();
        AppendLog("[timer-alarm] Reset to default beep.");
    }
}
