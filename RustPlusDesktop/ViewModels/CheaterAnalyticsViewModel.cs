using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.ViewModels
{
    public class CheaterAnalyticsViewModel : INotifyPropertyChanged
    {
        private readonly CheaterAnalyticsService _svc;
        private readonly SteamBanLookupService   _steamSvc;
        private readonly CheaterReportService    _reportSvc;
        private readonly string _serverId;
        private readonly string _serverName;
        private string _wipeId;

        public ObservableCollection<CheaterRecord> Records { get; } = new();
        public ObservableCollection<CheaterAnalyticsSnapshot> History { get; } = new();

        public IEnumerable<ConfidenceLevel> ConfidenceLevels =>
            Enum.GetValues<ConfidenceLevel>();
        public IEnumerable<FlagSource> FlagSources =>
            Enum.GetValues<FlagSource>();

        // ── bound properties ──────────────────────────────────────────────────

        private int _activePlayers;
        public int ActivePlayers
        {
            get => _activePlayers;
            set { _activePlayers = value; OnPropertyChanged(); RefreshMetrics(); }
        }

        private CheaterAnalyticsSnapshot _current = new();
        public CheaterAnalyticsSnapshot Current
        {
            get => _current;
            private set { _current = value; OnPropertyChanged(); }
        }

        // ── new record form fields ─────────────────────────────────────────────

        public string NewSteamId      { get; set; }
        public string NewName         { get; set; }
        public ConfidenceLevel NewConfidence { get; set; } = ConfidenceLevel.Low;
        public FlagSource      NewSource     { get; set; } = FlagSource.AdminManual;
        public string NewNotes        { get; set; }
        public string NewEvidenceLink { get; set; }

        // ── report/export properties ──────────────────────────────────────────

        public string DiscordWebhookUrl { get; set; }

        private string _reportStatus;
        public string ReportStatus
        {
            get => _reportStatus;
            set { _reportStatus = value; OnPropertyChanged(); }
        }

        // ── commands ──────────────────────────────────────────────────────────

        public ICommand AddRecordCommand    { get; }
        public ICommand ConfirmBanCommand   { get; }
        public ICommand DeleteRecordCommand { get; }
        public ICommand ExportCsvCommand    { get; }
        public ICommand ExportTxtCommand    { get; }
        public ICommand SendDiscordCommand  { get; }

        public CheaterAnalyticsViewModel(
            CheaterAnalyticsService svc,
            SteamBanLookupService steamSvc,
            string serverId,
            string wipeId,
            string serverName = null)
        {
            _svc        = svc;
            _steamSvc   = steamSvc;
            _serverId   = serverId;
            _serverName = serverName ?? serverId;
            _wipeId     = wipeId;
            _reportSvc  = new CheaterReportService();

            AddRecordCommand    = new AsyncRelayCommand(AddRecordWithSteamLookupAsync);
            ConfirmBanCommand   = new RelayCommand<CheaterRecord>(ConfirmBan);
            DeleteRecordCommand = new RelayCommand<CheaterRecord>(DeleteRecord);
            ExportCsvCommand    = new RelayCommand<object>(_ => ExportCsv());
            ExportTxtCommand    = new RelayCommand<object>(_ => ExportTxt());
            SendDiscordCommand  = new AsyncRelayCommand(SendDiscordAsync);

            Reload();
        }

        private void ExportCsv()
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var date    = DateTime.UtcNow.ToString("yyyyMMdd");
                var path    = Path.Combine(desktop, $"cheater_report_{_serverId}_{date}.csv");
                _reportSvc.SaveCsv(Records, path);
                ReportStatus = "✅ CSV saved to Desktop";
            }
            catch (Exception ex)
            {
                ReportStatus = $"❌ CSV error: {ex.Message}";
            }
        }

        private void ExportTxt()
        {
            try
            {
                var text    = _reportSvc.BuildF7ReportText(Records);
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var date    = DateTime.UtcNow.ToString("yyyyMMdd");
                var path    = Path.Combine(desktop, $"cheater_report_{_serverId}_{date}.txt");
                File.WriteAllText(path, text, System.Text.Encoding.UTF8);
                System.Windows.Clipboard.SetText(text);
                ReportStatus = "✅ TXT saved to Desktop";
            }
            catch (Exception ex)
            {
                ReportStatus = $"❌ TXT error: {ex.Message}";
            }
        }

        private async Task SendDiscordAsync()
        {
            if (string.IsNullOrWhiteSpace(DiscordWebhookUrl))
            {
                ReportStatus = "⚠️ No webhook URL set";
                return;
            }
            try
            {
                await _reportSvc.SendDiscordReportAsync(DiscordWebhookUrl, _serverName, Records);
                ReportStatus = "✅ Sent to Discord";
            }
            catch (Exception ex)
            {
                ReportStatus = $"❌ Discord error: {ex.Message}";
            }
        }

        public void Reload()
        {
            Records.Clear();
            foreach (var r in _svc.LoadRecords(_serverId))
                Records.Add(r);

            History.Clear();
            foreach (var s in _svc.LoadSnapshotHistory(_serverId))
                History.Add(s);

            RefreshMetrics();
        }

        private void RefreshMetrics()
        {
            Current = _svc.BuildSnapshot(_serverId, ActivePlayers,
                                         _wipeId, Records.ToList());
        }

        public async Task AddRecordWithSteamLookupAsync()
        {
            if (string.IsNullOrWhiteSpace(NewSteamId)) return;

            var record = new CheaterRecord
            {
                SteamId       = NewSteamId.Trim(),
                DisplayName   = NewName?.Trim() ?? "Unknown",
                Confidence    = NewConfidence,
                Source        = NewSource,
                EvidenceNotes = NewNotes,
                EvidenceLink  = NewEvidenceLink,
                FlaggedAt     = DateTime.UtcNow,
                WipeId        = _wipeId
            };

            try
            {
                var (vac, game, days) =
                    await _steamSvc.GetBanStatusAsync(NewSteamId);
                record.HasVacBan        = vac;
                record.HasGameBan       = game;
                record.DaysSinceLastBan = days;
                if (vac || game)
                    record.Confidence = ConfidenceLevel.Confirmed;
            }
            catch { /* Steam API unavailable — proceed without ban data */ }

            Records.Add(record);
            _svc.SaveRecords(_serverId, Records.ToList());
            RefreshMetrics();
            _svc.AppendSnapshot(_serverId, Current);
        }

        public void ConfirmBan(CheaterRecord record)
        {
            record.IsConfirmedBanned = true;
            record.Confidence        = ConfidenceLevel.Confirmed;
            record.BanConfirmedAt    = DateTime.UtcNow;
            _svc.SaveRecords(_serverId, Records.ToList());
            RefreshMetrics();
        }

        public void DeleteRecord(CheaterRecord record)
        {
            Records.Remove(record);
            _svc.SaveRecords(_serverId, Records.ToList());
            RefreshMetrics();
        }

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // Lightweight command helpers to avoid a full framework dependency
    internal class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private bool _running;

        public AsyncRelayCommand(Func<Task> execute) => _execute = execute;

        public bool CanExecute(object _) => !_running;
        public event EventHandler CanExecuteChanged;

        public async void Execute(object _)
        {
            _running = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try   { await _execute(); }
            finally
            {
                _running = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    internal class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;

        public RelayCommand(Action<T> execute) => _execute = execute;

        public bool CanExecute(object _) => true;
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter) => _execute((T)parameter);
    }
}
