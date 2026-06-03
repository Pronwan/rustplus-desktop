namespace RustPlusDesktop.Models
{
    public class CheaterAnalyticsSnapshot
    {
        public string ServerId { get; set; }
        public DateTime Timestamp { get; set; }
        public string WipeId { get; set; }
        public int ActivePlayerCount { get; set; }
        public int ConfirmedCheaterCount { get; set; }
        public int SuspectedFlaggedCount { get; set; }

        public double CheaterRatioPercent =>
            ActivePlayerCount > 0
                ? (ConfirmedCheaterCount + SuspectedFlaggedCount) * 100.0 / ActivePlayerCount
                : 0;

        public string RiskBand =>
            CheaterRatioPercent switch
            {
                < 5  => "Low",
                < 15 => "Moderate",
                < 30 => "High",
                _    => "Critical"
            };
    }
}
