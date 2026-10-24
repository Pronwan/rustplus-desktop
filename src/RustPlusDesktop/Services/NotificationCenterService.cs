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
                            Reword(notif);
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

            // Rows without a picture of their own (WebSocket alarms, oil rig timers) borrow the
            // one the same server's last push carried, so every row of a server looks the same.
            if (string.IsNullOrEmpty(notification.ImageUrl))
            {
                notification.ImageUrl = ImageForServer(notification.ServerIp, notification.ServerPort);
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

        /// <summary>
        /// Fills in the picture on this server's rows that have none. Each changed row is put
        /// back into the collection so the list redraws it; the model raises no change events.
        /// </summary>
        public static void BackfillServerImage(string ip, int port, string imageUrl)
        {
            EnsureLoaded();
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                bool changed = false;
                lock (LockObj)
                {
                    for (int i = 0; i < Notifications.Count; i++)
                    {
                        var n = Notifications[i];
                        if (n.ServerIp != ip || n.ServerPort != port || !string.IsNullOrEmpty(n.ImageUrl)) continue;
                        n.ImageUrl = imageUrl;
                        Notifications[i] = n;
                        changed = true;
                    }
                }
                if (changed) SaveHistory();
            });
        }

        private static string? ImageForServer(string? ip, int? port)
        {
            if (string.IsNullOrEmpty(ip)) return null;
            lock (LockObj)
            {
                return Notifications.FirstOrDefault(n =>
                    n.ServerIp == ip && n.ServerPort == port && !string.IsNullOrEmpty(n.ImageUrl))?.ImageUrl;
            }
        }

        /// <summary>
        /// Brings rows saved before the live-map look to its wording: the killer or the paired
        /// thing as the title, the server as the line under it. Only the two old fixed titles
        /// are touched, so anything else is left exactly as it was received.
        /// </summary>
        private static void Reword(RustPlusNotification n)
        {
            if (n.Type == "Death" && n.Title == "Offline Death" && !string.IsNullOrWhiteSpace(n.AttackerName))
            {
                n.Title = $"You were killed by {n.AttackerName}";
                if (!string.IsNullOrWhiteSpace(n.ServerName)) n.Message = n.ServerName;
            }
            else if (n.Type == "Pairing" && n.Title == "Pairing Successful")
            {
                if (n.EntityId.HasValue)
                {
                    n.Title = n.EntityName ?? "Smart Device";
                    n.Message = $"Device paired (ID {n.EntityId.Value})";
                }
                else if (!string.IsNullOrWhiteSpace(n.ServerName))
                {
                    n.Title = n.ServerName;
                    n.Message = "Server paired.";
                }
            }
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
