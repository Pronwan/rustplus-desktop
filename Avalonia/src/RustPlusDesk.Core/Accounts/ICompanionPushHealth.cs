namespace RustPlusDesk.Core.Accounts;

public enum PushHealthOutcome { Healthy, TokenRejected, NotDelivered, Inconclusive, SignInRequired }

/// <summary>A safe, point-in-time push-delivery result; only actual probe arrival proves delivery.</summary>
public sealed record PushHealthReport(PushHealthOutcome Outcome, string Message)
{
    public bool NeedsRepair => Outcome is PushHealthOutcome.TokenRejected or PushHealthOutcome.NotDelivered;
}

public interface ICompanionPushHealth
{
    Task<PushHealthReport> CheckAsync(string profilesPath, CancellationToken cancellationToken);
}
