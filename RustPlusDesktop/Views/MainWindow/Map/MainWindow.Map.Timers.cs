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
            TxtTimerValidation.Text = "";
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
        if (string.IsNullOrWhiteSpace(name) || !char.IsLetter(name[0]))
        {
            TxtTimerValidation.Text = Properties.Resources.TimerNameMustStartWithLetter;
            return;
        }

        int hours = int.TryParse(TxtTimerHours.Text, out int h) ? h : 0;
        int mins = int.TryParse(TxtTimerMinutes.Text, out int m) ? m : 0;
        int secs = int.TryParse(TxtTimerSeconds.Text, out int s) ? s : 0;

        if (hours == 0 && mins == 0 && secs == 0)
        {
            TxtTimerValidation.Text = Properties.Resources.TimerDurationRequired;
            return;
        }
        TxtTimerValidation.Text = "";

        var cmd = TxtTimerCommandPreview.Text;
        if (string.IsNullOrWhiteSpace(cmd)) cmd = name.ToLower();

        int totalSecs = hours * 3600 + mins * 60 + secs;
        double totalMins = totalSecs / 60.0;
        var timer = new CustomTimer
        {
            Name = name,
            Command = cmd,
            EndTimeUtc = DateTime.UtcNow.AddSeconds(totalSecs),
            CreatedNotified = false,
            Notified60 = totalMins <= 60,
            Notified30 = totalMins <= 30,
            Notified10 = totalMins <= 10,
            Notified3 = totalMins <= 3,
            EnableCountdownAudio = TglAddTimerCountdown.IsChecked == true,
            EnableAlarmAudio = TglAddTimerAlarm.IsChecked == true
        };

        _vm.Selected.CustomTimers.Add(timer);

        TxtTimerName.Text = "";
        TxtTimerHours.Text = "";
        TxtTimerMinutes.Text = "";
        TxtTimerSeconds.Text = "";

        PopupCustomTimer.IsOpen = false;

        if (_vm.Selected.AlertCustomTimer)
        {
            var msg = string.Format(Properties.Resources.TimerCreated, _vm.Selected.ChatCommandPrefix + cmd, hours, mins, secs);
            _ = SendTeamChatSafeAsync(msg, false, true);
            _ = RustPlusDesk.Services.DiscordBotListenerService.Instance.SendNotificationAsync("events", $"⏱️ **Timer:** {msg}");
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

            if (remaining.TotalSeconds <= -60)
            {
                toRemove.Add(timer);
                continue;
            }

            if (remaining.TotalSeconds <= 60 && remaining.TotalSeconds > 0)
            {
                if (!timer.CountdownAudioPlayed)
                {
                    timer.CountdownAudioPlayed = true;
                    if (timer.EnableCountdownAudio)
                    {
                        PlayTimerAudio(true);
                        ShowTimerSnackbar("1 min countdown...", timer.Name, 60);
                    }
                }
            }
            else if (remaining.TotalSeconds <= 0)
            {
                if (_vm.Selected.AlertCustomTimer && remaining.TotalSeconds >= -60 && !timer.AlarmPlayed)
                {
                    string msg = $"{timer.Name}: 00:00";
                    _ = SendTeamChatSafeAsync(msg, false, true);
                    _ = RustPlusDesk.Services.DiscordBotListenerService.Instance.SendNotificationAsync("events", $"⏱️ **Timer:** {msg}");
                }
                if (!timer.AlarmPlayed)
                {
                    timer.AlarmPlayed = true;
                    if (timer.EnableAlarmAudio)
                    {
                        PlayTimerAudio(false);
                        ShowTimerSnackbar("Timer Expired", timer.Name, 15);
                    }
                }
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
                        string msg = $"{timer.Name}: 60:00";
                        _ = SendTeamChatSafeAsync(msg, false, true);
                        _ = RustPlusDesk.Services.DiscordBotListenerService.Instance.SendNotificationAsync("events", $"⏱️ **Timer:** {msg}");
                    }
                }
                if (remaining.TotalMinutes <= 30 && !timer.Notified30)
                {
                    timer.Notified30 = true;
                    if (remaining.TotalMinutes >= 29)
                    {
                        string msg = $"{timer.Name}: 30:00";
                        _ = SendTeamChatSafeAsync(msg, false, true);
                        _ = RustPlusDesk.Services.DiscordBotListenerService.Instance.SendNotificationAsync("events", $"⏱️ **Timer:** {msg}");
                    }
                }
                if (remaining.TotalMinutes <= 10 && !timer.Notified10)
                {
                    timer.Notified10 = true;
                    if (remaining.TotalMinutes >= 9)
                    {
                        string msg = $"{timer.Name}: 10:00";
                        _ = SendTeamChatSafeAsync(msg, false, true);
                        _ = RustPlusDesk.Services.DiscordBotListenerService.Instance.SendNotificationAsync("events", $"⏱️ **Timer:** {msg}");
                    }
                }
                if (remaining.TotalMinutes <= 3 && !timer.Notified3)
                {
                    timer.Notified3 = true;
                    if (remaining.TotalMinutes >= 2)
                    {
                        string msg = $"{timer.Name}: 03:00";
                        _ = SendTeamChatSafeAsync(msg, false, true);
                        _ = RustPlusDesk.Services.DiscordBotListenerService.Instance.SendNotificationAsync("events", $"⏱️ **Timer:** {msg}");
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

    public void StopTimerAudio()
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

    private async void PlayTimerAudio(bool isCountdown)
    {
        if (_vm.Selected == null) return;

        try
        {
            string audioFile;

            if (isCountdown)
            {
                if (!string.IsNullOrWhiteSpace(_vm.Selected.TimerCountdownAudioPath))
                    audioFile = _vm.Selected.TimerCountdownAudioPath;
                else
                {
                    string baseDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                    audioFile = System.IO.Path.Combine(baseDir, "Assets", "1min.mp3");
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_vm.Selected.TimerAlarmAudioPath))
                    audioFile = _vm.Selected.TimerAlarmAudioPath;
                else
                {
                    string baseDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
                    audioFile = System.IO.Path.Combine(baseDir, "Assets", "bell.mp3");
                }
            }

            if (System.IO.File.Exists(audioFile))
            {
                var fullPath = System.IO.Path.GetFullPath(audioFile);
                Dispatcher.Invoke(() =>
                {
                    StopTimerAudio();
                    _timerAlarmPlayer = new System.Windows.Media.MediaPlayer();
                    _timerAlarmPlayer.MediaFailed += (s, e) => AppendLog($"[timer-audio] Media Failed: {e.ErrorException?.Message}");
                    _timerAlarmPlayer.MediaEnded += (s, e) =>
                    {
                        AppendLog("[timer-audio] Playback ended.");
                        Dispatcher.Invoke(() =>
                        {
                            if (BtnStopTimerAlarm != null) BtnStopTimerAlarm.Visibility = Visibility.Collapsed;
                            if (BtnSnoozeTimerAlarm != null) BtnSnoozeTimerAlarm.Visibility = Visibility.Collapsed;
                        });
                    };
                    _timerAlarmFilePath = fullPath;
                    _timerAlarmPlayer.Open(new Uri(fullPath, UriKind.Absolute));
                    _timerAlarmPlayer.Volume = 1.0;
                    _timerAlarmPlayer.Play();
                    if (BtnStopTimerAlarm != null) BtnStopTimerAlarm.Visibility = Visibility.Visible;
                    if (BtnSnoozeTimerAlarm != null) BtnSnoozeTimerAlarm.Visibility = Visibility.Visible;
                    AppendLog($"[timer-audio] Playing: {fullPath}");
                });
            }
            else
            {
                if (!isCountdown)
                {
                    int duration = _vm.Selected.TimerAlarmBeepDurationSeconds;
                    for (int i = 0; i < duration; i++)
                    {
                        System.Media.SystemSounds.Beep.Play();
                        await Task.Delay(1000);
                    }
                    Dispatcher.Invoke(() =>
                    {
                        if (BtnStopTimerAlarm != null) BtnStopTimerAlarm.Visibility = Visibility.Collapsed;
                        if (BtnSnoozeTimerAlarm != null) BtnSnoozeTimerAlarm.Visibility = Visibility.Collapsed;
                    });
                }
                else
                {
                    // Fallback for countdown if file doesn't exist: still show the buttons!
                    Dispatcher.Invoke(() =>
                    {
                        if (BtnStopTimerAlarm != null) BtnStopTimerAlarm.Visibility = Visibility.Visible;
                        if (BtnSnoozeTimerAlarm != null) BtnSnoozeTimerAlarm.Visibility = Visibility.Visible;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[timer-alarm] Error playing audio: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                if (BtnStopTimerAlarm != null) BtnStopTimerAlarm.Visibility = Visibility.Collapsed;
                if (BtnSnoozeTimerAlarm != null) BtnSnoozeTimerAlarm.Visibility = Visibility.Collapsed;
            });
            try { System.Media.SystemSounds.Beep.Play(); } catch { }
        }
    }

    private void BtnStopTimerAlarm_Click(object sender, RoutedEventArgs e)
    {
        DismissAlarm();
    }

    public void DismissAlarm()
    {
        StopTimerAudio();
        if (_vm.Selected != null)
        {
            var toDelete = _vm.Selected.CustomTimers.Where(t => t.CountdownAudioPlayed || t.AlarmPlayed).ToList();
            foreach (var t in toDelete)
            {
                _vm.Selected.CustomTimers.Remove(t);
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
        StopTimerAudio();
        
        int snoozeMins = _vm.Selected.TimerAlarmSnoozeMinutes;
        var now = DateTime.UtcNow;
        
        foreach (var timer in _vm.Selected.CustomTimers)
        {
            var remaining = timer.EndTimeUtc - now;
            if (timer.CountdownAudioPlayed || timer.AlarmPlayed)
            {
                if (remaining.TotalSeconds <= 0)
                {
                    timer.EndTimeUtc = now.AddMinutes(snoozeMins);
                    timer.CountdownAudioPlayed = false;
                    timer.AlarmPlayed = false;
                    timer.Notified60 = false;
                    timer.Notified30 = false;
                    timer.Notified10 = false;
                    timer.Notified3 = false;
                }
            }
        }
        AppendLog($"[timer-alarm] Snoozed.");
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

    private void BtnSelectTimerCountdownAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected == null) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.wav|All Files|*.*",
            Title = "Select Timer 1min Countdown Sound"
        };

        if (dlg.ShowDialog() == true)
        {
            _vm.Selected.TimerCountdownAudioPath = dlg.FileName;
            _vm.Save();
            AppendLog($"[timer-countdown] Selected audio: {dlg.SafeFileName}");
        }
    }

    private void BtnResetTimerAlarmAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Selected == null) return;
        _vm.Selected.TimerAlarmAudioPath = null;
        _vm.Selected.TimerCountdownAudioPath = null;
        _vm.Save();
        AppendLog("[timer-audio] Reset to default embedded sounds.");
    }
}
