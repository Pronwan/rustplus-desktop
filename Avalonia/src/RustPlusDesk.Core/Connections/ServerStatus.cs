namespace RustPlusDesk.Core.Connections;

/// <summary>Portable, credential-free server status received from the companion protocol.</summary>
public sealed record ServerStatus(string Name, uint? Players, uint? MaxPlayers, uint? Queue,
    double? GameHours = null, double? Sunrise = null, double? Sunset = null,
    double? DayLengthMinutes = null, double? TimeScale = null);

public enum ConnectionPhase { Disconnected, Connecting, Connected, Reconnecting, Failed, RePairRequired, SignInRequired }

public enum ConnectionFailure { InvalidCredentials, ProfileUnavailable, RePairRequired, CompanionLoginRequired, Unreachable }

/// <summary>A classified connection failure with no protocol payloads or credentials in its message.</summary>
public sealed class ServerConnectionException(ConnectionFailure failure) : Exception(failure.ToString())
{
    public ConnectionFailure Failure { get; } = failure;
}
