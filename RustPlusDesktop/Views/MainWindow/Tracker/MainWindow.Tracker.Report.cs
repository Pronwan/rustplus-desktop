using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _reportWebViewInitialized;
    private bool _reportPanelOpen;

    /// <summary>(bmId, serverId) → last successful backfill time. Used to skip redundant network round-trips.</summary>
    private readonly Dictionary<(string bmId, string serverId), DateTime> _lastBackfillAt = new();
    private static readonly TimeSpan _backfillFreshFor = TimeSpan.FromMinutes(10);

    /// <summary>(bmId, serverId) currently being backfilled, to suppress concurrent duplicate fetches.</summary>
    private readonly HashSet<(string bmId, string serverId)> _backfillInFlight = new();

    /// <summary>
    /// Open the inline report panel (slide-in from right) and load the analysis HTML.
    /// </summary>
    private async void OpenInlineReport(string bmId, string playerName)
    {
        if (ReportPanel == null || ReportWebView == null) return;

        if (TxtReportTitle != null)
            TxtReportTitle.Text = string.IsNullOrEmpty(playerName)
                ? "Player Activity Report"
                : $"Activity Report — {playerName}";

        // Position offscreen-right before showing, so the slide animation has somewhere to come from.
        ReportPanel.Visibility = Visibility.Visible;
        ReportPanel.UpdateLayout();
        var w = ReportPanel.ActualWidth > 0 ? ReportPanel.ActualWidth : 700;
        if (ReportPanelTransform != null) ReportPanelTransform.X = w;

        // Capture Esc to close.
        this.PreviewKeyDown -= ReportPanel_PreviewKeyDown;
        this.PreviewKeyDown += ReportPanel_PreviewKeyDown;

        AnimatePanelTo(0);
        _reportPanelOpen = true;
        OnSlideInPanelOpened();

        try
        {
            if (!_reportWebViewInitialized)
            {
                var dataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RustPlusDesk-Ryyott", "WebView2_Report");
                Directory.CreateDirectory(dataPath);
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataPath);
                await ReportWebView.EnsureCoreWebView2Async(env);
                _reportWebViewInitialized = true;
            }
            ReportWebView.NavigateToString(BuildLoadingHtml(playerName, "Loading report…"));

            var serverId = TrackingService.CurrentServerBMId;
            int imported = 0;

            if (!string.IsNullOrEmpty(serverId))
            {
                var key = (bmId, serverId);
                bool alreadyInFlight = _backfillInFlight.Contains(key);
                bool fresh = _lastBackfillAt.TryGetValue(key, out var when)
                             && (DateTime.UtcNow - when) < _backfillFreshFor;

                if (!alreadyInFlight && !fresh)
                {
                    _backfillInFlight.Add(key);
                    try
                    {
                        ReportWebView.NavigateToString(BuildLoadingHtml(playerName,
                            "Pulling last 90 days from BattleMetrics…"));
                        imported = await TrackingService.BackfillSessionsFromBMAsync(bmId, serverId, daysBack: 90);
                        _lastBackfillAt[key] = DateTime.UtcNow;
                        AppendLog($"[report] Imported {imported} BM sessions for {playerName} on this server.");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"[report] BM backfill failed: {ex.Message}");
                    }
                    finally
                    {
                        _backfillInFlight.Remove(key);
                    }
                }
            }

            var html = await TrackingService.GetAnalysisReportForBMIdAsync(bmId, playerName, serverId);
            ReportWebView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            AppendLog($"[report] Failed to load report: {ex.Message}");
        }
    }

    private static string BuildLoadingHtml(string playerName, string subText)
    {
        var safe = System.Net.WebUtility.HtmlEncode(playerName ?? "");
        var safeSub = System.Net.WebUtility.HtmlEncode(subText ?? "");
        return "<!DOCTYPE html><html><head><meta charset='utf-8'><style>" +
               "body{background:#0d1117;color:#c9d1d9;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;margin:30px;}" +
               "h1{color:#f0f6fc;font-size:22px;font-weight:600;}" +
               ".sub{color:#8b949e;font-size:13px;margin-top:8px;}" +
               "</style></head><body>" +
               $"<h1>Loading report for {safe}…</h1>" +
               $"<div class='sub'>{safeSub}</div>" +
               "</body></html>";
    }

    private void BtnCloseReport_Click(object sender, RoutedEventArgs e) => CloseInlineReport();

    private void CloseInlineReport()
    {
        if (ReportPanel == null || !_reportPanelOpen) return;
        var w = ReportPanel.ActualWidth > 0 ? ReportPanel.ActualWidth : 700;
        AnimatePanelTo(w, onCompleted: () =>
        {
            ReportPanel.Visibility = Visibility.Collapsed;
            // Free the navigated content so the next open starts clean.
            try { ReportWebView?.NavigateToString("<html><body></body></html>"); } catch { }
        });
        this.PreviewKeyDown -= ReportPanel_PreviewKeyDown;
        _reportPanelOpen = false;
        OnSlideInPanelClosed();
    }

    private void ReportPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // If hidden, keep it parked offscreen so a future show animates correctly.
        if (!_reportPanelOpen && ReportPanelTransform != null)
            ReportPanelTransform.X = e.NewSize.Width;
    }

    private void ReportPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _reportPanelOpen)
        {
            CloseInlineReport();
            e.Handled = true;
        }
    }

    private void AnimatePanelTo(double targetX, Action? onCompleted = null)
    {
        if (ReportPanelTransform == null) { onCompleted?.Invoke(); return; }
        var anim = new DoubleAnimation
        {
            To = targetX,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        if (onCompleted != null)
            anim.Completed += (_, __) => onCompleted();
        ReportPanelTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
    }

    // ─── Click handlers wired from XAML ─────────────────────────────────────

    private void BtnOnlineReport_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not OnlinePlayerBM p) return;
        OpenInlineReport(p.BMId, p.Name);
    }

    private void BtnGroupMemberReport_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not GroupMemberRow row) return;
        OpenInlineReport(row.BMId, row.Name);
    }
}
