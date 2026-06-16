using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services
{
    public static class NotificationCenterService
    {
        private static readonly object LockObj = new object();
        private static bool _loaded = false;

        public static ObservableCollection<RustPlusNotification> Notifications { get; } = new ObservableCollection<RustPlusNotification>();

        private static int _unreadCount = 0;
        public static int UnreadCount
        {
            get
            {
                EnsureLoaded();
                return _unreadCount;
            }
            private set
            {
                if (_unreadCount != value)
                {
                    _unreadCount = value;
                    UnreadCountChanged?.Invoke(null, _unreadCount);
                }
            }
        }

        public static event EventHandler<int>? UnreadCountChanged;
        public static event EventHandler<RustPlusNotification>? NotificationAdded;
        public static event EventHandler? HistoryCleared;

        private static void EnsureLoaded()
        {
            lock (LockObj)
            {
                if (_loaded) return;
                _loaded = true;
                LoadHistory();
            }
        }

        public static void LoadHistory()
        {
            lock (LockObj)
            {
                try
                {
                    var cached = DataManager.LoadCache<List<RustPlusNotification>>("notifications_history");
                    Notifications.Clear();
                    if (cached != null)
                    {
                        // Clean up old notifications if needed based on retention
                        var cutoff = DateTime.Now.AddDays(-TrackingService.NotificationsRetentionDays);
                        var validNotifications = cached.Where(n => n.Timestamp >= cutoff).OrderByDescending(n => n.Timestamp).ToList();

                        foreach (var notif in validNotifications)
                        {
                            Notifications.Add(notif);
                        }
                    }
                    UpdateUnreadCount();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NotificationCenter] Error loading history: {ex.Message}");
                }
            }
        }

        public static void SaveHistory()
        {
            lock (LockObj)
            {
                try
                {
                    var list = Notifications.ToList();
                    DataManager.SaveCache("notifications_history", list);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NotificationCenter] Error saving history: {ex.Message}");
                }
            }
        }

        public static void AddNotification(RustPlusNotification notification)
        {
            EnsureLoaded();

            // Check if server is muted
            if (!string.IsNullOrEmpty(notification.ServerIp) && notification.ServerPort.HasValue)
            {
                string serverKey = $"{notification.ServerIp}:{notification.ServerPort.Value}";
                if (TrackingService.MutedNotificationServers.Contains(serverKey))
                {
                    return; // Muted!
                }
            }

            lock (LockObj)
            {
                // Persistent de-duplication by FCM message id so stopping/restarting the listener
                // does not re-add the same push notification.
                if (!string.IsNullOrEmpty(notification.FcmNotificationId) &&
                    Notifications.Any(n => n.FcmNotificationId == notification.FcmNotificationId))
                {
                    return;
                }

                // De-duplication check: if a notification with same Type, Message, Server name/IP
                // is already in the list within 4 seconds, skip it to prevent double alerts (FCM + WS bounce).
                var duplicate = Notifications.FirstOrDefault(n =>
                    n.Type == notification.Type &&
                    n.Message == notification.Message &&
                    n.ServerIp == notification.ServerIp &&
                    n.ServerPort == notification.ServerPort &&
                    Math.Abs((n.Timestamp - notification.Timestamp).TotalSeconds) < 4);

                if (duplicate != null)
                {
                    return;
                }

                // Add to list (UI-thread safe if needed, but we invoke on Dispatcher in view usually)
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Notifications.Insert(0, notification);

                    // Enforce retention limit (e.g. days or max items)
                    var cutoff = DateTime.Now.AddDays(-TrackingService.NotificationsRetentionDays);
                    for (int i = Notifications.Count - 1; i >= 0; i--)
                    {
                        if (Notifications[i].Timestamp < cutoff)
                        {
                            Notifications.RemoveAt(i);
                        }
                    }

                    // Keep total count capped at 500 for performance
                    while (Notifications.Count > 500)
                    {
                        Notifications.RemoveAt(Notifications.Count - 1);
                    }
                });

                UpdateUnreadCount();
                SaveHistory();
            }

            NotificationAdded?.Invoke(null, notification);
        }

        public static void MarkAsRead(string id)
        {
            EnsureLoaded();
            lock (LockObj)
            {
                var notif = Notifications.FirstOrDefault(n => n.Id == id);
                if (notif != null && !notif.IsRead)
                {
                    notif.IsRead = true;
                    UpdateUnreadCount();
                    SaveHistory();
                }
            }
        }

        public static void MarkAllAsRead()
        {
            EnsureLoaded();
            lock (LockObj)
            {
                bool changed = false;
                foreach (var notif in Notifications)
                {
                    if (!notif.IsRead)
                    {
                        notif.IsRead = true;
                        changed = true;
                    }
                }
                if (changed)
                {
                    UnreadCount = 0;
                    SaveHistory();
                }
            }
        }

        public static void DeleteNotification(string id)
        {
            EnsureLoaded();
            lock (LockObj)
            {
                var notif = Notifications.FirstOrDefault(n => n.Id == id);
                if (notif != null)
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Notifications.Remove(notif);
                    });
                    UpdateUnreadCount();
                    SaveHistory();
                }
            }
        }

        public static void ClearHistory()
        {
            EnsureLoaded();
            lock (LockObj)
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Notifications.Clear();
                });
                UnreadCount = 0;
                SaveHistory();
                HistoryCleared?.Invoke(null, EventArgs.Empty);
            }
        }

        private static void UpdateUnreadCount()
        {
            UnreadCount = Notifications.Count(n => !n.IsRead);
        }
    }
}
