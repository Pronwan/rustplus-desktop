namespace RustPlusDesk.Models;

public enum ConnectionFailureKind
{
    Unavailable,
    MissingCompanionId,
    ServerError,
    Unsupported,
    RePairRequired,
    SignInRequired,
    /// <summary>Windows Application Control blocked RustPlus.Auth.dll, so the login cannot be read.</summary>
    AuthBlocked
}
