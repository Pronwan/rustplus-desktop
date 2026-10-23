namespace RustPlusDesk.Core.Accounts;

public enum CompanionUpdateKind { Connecting, Listening, Reconnecting, Pairing, Alarm, Chat, Death, Notice }

/// <summary>A credential-free push or listener status, safe for presentation and history.</summary>
public sealed record CompanionUpdate(CompanionUpdateKind Kind, string Title, string Message)
{
    public string ServerName { get; init; } = "";
    public string Host { get; init; } = "";
    public int? Port { get; init; }
    public uint? EntityId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? ProfileKey { get; init; }
    public string? ReplacesProfileKey { get; init; }
    public bool ProfilesChanged { get; init; }
}

/// <summary>Owns push transport and secret-bearing pairing writes until enumeration is canceled/disposed.</summary>
public interface ICompanionListener
{
    IAsyncEnumerable<CompanionUpdate> ListenAsync(string profilesPath, CancellationToken cancellationToken);
}

public enum CompanionListenerFailure { MissingCredentials, InvalidCredentials, StorageUnavailable }

public sealed class CompanionListenerException(CompanionListenerFailure failure) : Exception(failure.ToString())
{
    public CompanionListenerFailure Failure { get; } = failure;
}
