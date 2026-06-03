namespace RustPlusDesk.Models
{
    public enum ConfidenceLevel { Low, Medium, High, Confirmed }
    public enum FlagSource { AdminManual, PlayerReport, VACBan, GameBan, BattleMetrics }

    public class CheaterRecord
    {
        public string SteamId { get; set; }
        public string DisplayName { get; set; }
        public ConfidenceLevel Confidence { get; set; }
        public FlagSource Source { get; set; }
        public bool IsConfirmedBanned { get; set; }
        public int ReportCount { get; set; }
        public bool HasVacBan { get; set; }
        public bool HasGameBan { get; set; }
        public int DaysSinceLastBan { get; set; }
        public string EvidenceNotes { get; set; }
        public string EvidenceLink { get; set; }
        public DateTime FlaggedAt { get; set; }
        public DateTime? BanConfirmedAt { get; set; }
        public string WipeId { get; set; }
    }
}
