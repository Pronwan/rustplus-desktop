namespace RustPlusWeb.Data;

/// <summary>A bounded, durable record of a smart-alarm or server-timer alert.</summary>
public sealed class AlarmEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string RustServerId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}
