namespace RustPlusDesk.Models;

public class DownloadReport
{
    public double Progress { get; set; }
    public string Percentage { get; set; } = "0%";
    public string Speed { get; set; } = "0 B/s";
    public string BytesReceived { get; set; } = "0 B";
    public string TotalBytes { get; set; } = "0 B";
}
